using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.BackgroundWorker;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Services;
using LgymApi.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;


namespace LgymApi.IntegrationTests;

/// <summary>
/// Base class for integration tests providing web application factory, HTTP client, and common test helpers.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    /// <summary>
    /// Gets the web application factory for hosting the API during tests.
    /// </summary>
    protected CustomWebApplicationFactory Factory { get; private set; } = null!;

    /// <summary>
    /// Gets the HTTP client for sending requests to the test API.
    /// </summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Initializes the test environment before each test by creating a fresh factory and client.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        Factory = new CustomWebApplicationFactory();
        Client = Factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        Client.Dispose();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureDeleted();

        Factory.Dispose();
    }

    public void Dispose()
    {
        Client?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }

    // Default admin credentials for tests
    protected const string AdminName = TestDataFactory.DefaultAdminName;
    protected const string AdminEmail = TestDataFactory.DefaultAdminEmail;
    protected const string AdminPassword = TestDataFactory.DefaultAdminSecret;

    /// <summary>
    /// Seeds a default admin user into the test database and returns the created user entity.
    /// </summary>
    protected async Task<User> SeedAdminAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.SeedAdminAsync(db);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Seeds a user with specified properties into the test database and returns the created user entity.
    /// </summary>
    protected async Task<User> SeedUserAsync(
        string name = "testuser",
        string email = "test@example.com",
        string password = "password123",
        bool isAdmin = false,
        bool isVisibleInRanking = true,
        bool isTester = false,
        bool isDeleted = false,
        int elo = 1000)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.SeedUserAsync(
            db,
            name,
            email,
            password,
            isAdmin,
            isVisibleInRanking,
            isTester,
            isDeleted,
            elo);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Generates a JWT token for the specified user with roles and permissions loaded from the database.
    /// </summary>
    protected string GenerateJwt(Id<User> userId, Id<UserSession> sessionId, string jti)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var roles = db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .Distinct()
            .ToList();

        var permissionClaims = db.UserRoles
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.RoleClaims)
            .Where(rc => rc.ClaimType == AuthConstants.PermissionClaimType)
            .Select(rc => rc.ClaimValue)
            .Distinct()
            .ToList();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new System.Security.Claims.Claim("userId", userId.ToString()),
            new System.Security.Claims.Claim("sid", sessionId.ToString()),
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Jti, jti)
        };

        foreach (var role in roles)
        {
            claims.Add(new System.Security.Claims.Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in permissionClaims)
        {
            claims.Add(new System.Security.Claims.Claim(AuthConstants.PermissionClaimType, permission));
        }

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Creates a user session, generates a JWT token, and sets the Authorization header for subsequent requests.
    /// </summary>
    protected void SetAuthorizationHeader(Id<User> userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var jti = Id<UserSession>.New().ToString();
        var session = new UserSession
        {
            Id = Id<UserSession>.New(),
            UserId = userId,
            Jti = jti,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAtUtc = null
        };

        db.UserSessions.Add(session);
        db.SaveChanges();

        var token = GenerateJwt(userId, session.Id, session.Jti);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Clears the Authorization header from the HTTP client.
    /// </summary>
    protected void ClearAuthorizationHeader()
    {
        Client.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>
    /// Returns a scoped database context for manual queries and assertions.
    /// </summary>
    protected AppDbContext GetDbContext()
    {
        var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    /// <summary>
    /// Posts JSON content with API serialization options (enum-as-string, camelCase, ignore nulls) and auto-generates Idempotency-Key if not present.
    /// </summary>
    protected async Task<HttpResponseMessage> PostAsJsonWithApiOptionsAsync<T>(string requestUri, T value)
    {
        // Auto-set idempotency key if not already present (for idempotent mutating endpoints)
        bool hadIdempotencyKey = Client.DefaultRequestHeaders.Contains("Idempotency-Key");
        bool shouldClearAfter = false;

        if (!hadIdempotencyKey)
        {
            // Generate unique key using timestamp ticks to avoid Guid.NewGuid() architecture violation
            var key = $"test-auto-{requestUri.Replace("/", "-")}-{DateTime.UtcNow.Ticks:X16}";
            SetIdempotencyKey(key);
            shouldClearAfter = true;
        }

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        var json = System.Text.Json.JsonSerializer.Serialize(value, options);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Await response to ensure it completes before cleanup
        var response = await Client.PostAsync(requestUri, content);

        // Clean up auto-set key after request completes (no race condition with await)
        if (shouldClearAfter)
        {
            ClearIdempotencyKey();
        }

        return response;
    }

    /// <summary>
    /// Registers a new user via the registration endpoint and returns the user ID and authentication token.
    /// </summary>
    protected async Task<(Id<User> UserId, string Token)> RegisterUserViaEndpointAsync(
        string name = "testuser",
        string email = "test@example.com",
        string password = "password123",
        bool isVisibleInRanking = true)
    {
        var registerRequest = new
        {
            name,
            email,
            password,
            cpassword = password,
            isVisibleInRanking
        };

        // Set idempotency key for registration endpoint (required by T9 middleware)
        // Use deterministic key based on email for test isolation
        SetIdempotencyKey($"test-register-{email}");

        var registerResponse = await Client.PostAsJsonAsync("/api/register", registerRequest);

        // Clear idempotency key after request
        ClearIdempotencyKey();

        if (!registerResponse.IsSuccessStatusCode)
        {
            var errorBody = await registerResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Registration failed with status {registerResponse.StatusCode}: {errorBody}");
        }

        var loginRequest = new { name, password };
        var loginResponse = await Client.PostAsJsonAsync("/api/login", loginRequest);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResult>();

        if (!Id<User>.TryParse(loginBody!.User!.Id!, out var userId))
        {
            throw new InvalidOperationException($"Failed to parse user ID: {loginBody.User.Id}");
        }

        return (userId, loginBody.Token!);
    }

    /// <summary>
    /// Creates a gym via the endpoint and returns the created gym ID.
    /// </summary>
    protected async Task<Id<Gym>> CreateGymViaEndpointAsync(Id<User> userId, string name = "Test Gym")
    {
        SetAuthorizationHeader(userId);
        var request = new { name, address = (string?)null };
        await Client.PostAsJsonAsync($"/api/gym/{userId}/addGym", request);

        var gymsResponse = await Client.GetAsync($"/api/gym/{userId}/getGyms");
        var gyms = await gymsResponse.Content.ReadFromJsonAsync<List<GymResult>>();

        if (!Id<Gym>.TryParse(gyms!.First(g => g.Name == name).Id!, out var gymId))
        {
            throw new InvalidOperationException($"Failed to parse gym ID");
        }

        return gymId;
    }

    /// <summary>
    /// Creates a training plan via the endpoint and returns the created plan ID.
    /// </summary>
    protected async Task<Id<Plan>> CreatePlanViaEndpointAsync(Id<User> userId, string name = "Test Plan")
    {
        SetAuthorizationHeader(userId);
        var request = new { name };
        await Client.PostAsJsonAsync($"/api/{userId}/createPlan", request);

        var plansResponse = await Client.GetAsync($"/api/{userId}/getPlansList");
        var plans = await plansResponse.Content.ReadFromJsonAsync<List<PlanResult>>();

        if (!Id<Plan>.TryParse(plans!.First(p => p.Name == name).Id!, out var planId))
        {
            throw new InvalidOperationException($"Failed to parse plan ID");
        }

        return planId;
    }

    /// <summary>
    /// Creates a user-specific exercise via the endpoint and returns the created exercise ID.
    /// </summary>
    protected async Task<Id<Exercise>> CreateExerciseViaEndpointAsync(
        Id<User> userId,
        string name = "Test Exercise",
        BodyParts bodyPart = BodyParts.Chest)
    {
        SetAuthorizationHeader(userId);
        var request = new { name, bodyPart = bodyPart.ToString(), description = "Test description" };
        await PostAsJsonWithApiOptionsAsync($"/api/exercise/{userId}/addUserExercise", request);

        var exercisesResponse = await Client.GetAsync($"/api/exercise/{userId}/getAllUserExercises");
        var exercises = await exercisesResponse.Content.ReadFromJsonAsync<List<ExerciseResult>>();

        if (!Id<Exercise>.TryParse(exercises!.First(e => e.Name == name).Id!, out var exerciseId))
        {
            throw new InvalidOperationException($"Failed to parse exercise ID");
        }

        return exerciseId;
    }

    /// <summary>
    /// Creates a global exercise via the endpoint (requires admin authorization) and returns the created exercise ID.
    /// </summary>
    protected async Task<Id<Exercise>> CreateGlobalExerciseViaEndpointAsync(
        Id<User> userId,
        string name = "Global Exercise",
        BodyParts bodyPart = BodyParts.Chest)
    {
        SetAuthorizationHeader(userId);
        var request = new { name, bodyPart = bodyPart.ToString(), description = "Global exercise description" };
        await Client.PostAsJsonAsync("/api/exercise/addExercise", request);

        var exercisesResponse = await Client.GetAsync("/api/exercise/getAllGlobalExercises");
        var exercises = await exercisesResponse.Content.ReadFromJsonAsync<List<ExerciseResult>>();

        if (!Id<Exercise>.TryParse(exercises!.First(e => e.Name == name).Id!, out var exerciseId))
        {
            throw new InvalidOperationException($"Failed to parse exercise ID");
        }

        return exerciseId;
    }

    /// <summary>
    /// Creates a plan day with exercises via the endpoint and returns the created plan day ID.
    /// </summary>
    protected async Task<Id<PlanDay>> CreatePlanDayViaEndpointAsync(Id<User> userId, Id<Plan> planId, string name, List<PlanDayExerciseInput> exercises)
    {
        SetAuthorizationHeader(userId);
        // Convert internal typed ID model to HTTP request DTO format (with string IDs)
        var exerciseDtos = exercises.Select(e => new { exercise = e.ExerciseId.ToString()!, series = e.Series, reps = e.Reps }).ToList();
        var request = new { name, exercises = exerciseDtos };
        await PostAsJsonWithApiOptionsAsync($"/api/planDay/{planId}/createPlanDay", request);

        var planDaysResponse = await Client.GetAsync($"/api/planDay/{planId}/getPlanDays");
        var planDays = await planDaysResponse.Content.ReadFromJsonAsync<List<PlanDayResult>>();

        if (!Id<PlanDay>.TryParse(planDays!.First(pd => pd.Name == name).Id!, out var planDayId))
        {
            throw new InvalidOperationException($"Failed to parse plan day ID");
        }

        return planDayId;
    }

    /// <summary>
    /// Response DTO for login endpoint containing JWT token and user information.
    /// </summary>
    protected sealed class LoginResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("token")]
        public string? Token { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("req")]
        public UserResult? User { get; set; }
    }

    /// <summary>
    /// Response DTO representing a user with ID and name.
    /// </summary>
    protected sealed class UserResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("_id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// Response DTO representing a gym with ID and name.
    /// </summary>
    protected sealed class GymResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("_id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// Response DTO representing a training plan with ID and name.
    /// </summary>
    protected sealed class PlanResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("_id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// Response DTO representing an exercise with ID and name.
    /// </summary>
    protected sealed class ExerciseResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("_id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// Response DTO representing a plan day with ID and name.
    /// </summary>
    protected sealed class PlanDayResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("_id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// Input model for creating plan day exercises with exercise ID, series count, and reps specification.
    /// </summary>
    protected sealed class PlanDayExerciseInput
    {
        [System.Text.Json.Serialization.JsonPropertyName("exercise")]
        public string? ExerciseId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("series")]
        public int Series { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("reps")]
        public string Reps { get; set; } = string.Empty;
    }

    /// <summary>
    /// Runs background command orchestration passes to process pending commands, with exception suppression and max-pass limit.
    /// </summary>
    protected async Task ProcessPendingCommandsAsync()
    {
        const int maxPasses = 5;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<BackgroundActionOrchestratorService>();

            var envelopes = await db.CommandEnvelopes
                .Include(ce => ce.ExecutionLogs)
                .Where(ce =>
                    ce.Status != ActionExecutionStatus.Completed
                    && ce.Status != ActionExecutionStatus.DeadLettered)
                .OrderBy(ce => ce.CreatedAt)
                .ToListAsync();

            if (envelopes.Count == 0)
            {
                break;
            }


            var normalizedAny = false;
            foreach (var envelope in envelopes)
            {
                var hasExecuteAttempt = envelope.ExecutionLogs.Any(log => log.ActionType == ActionExecutionLogType.Execute);
                if (envelope.Status == ActionExecutionStatus.Processing && !hasExecuteAttempt)
                {
                    envelope.Status = ActionExecutionStatus.Pending;
                    normalizedAny = true;
                }
            }

            if (normalizedAny)
            {
                await db.SaveChangesAsync();
            }

            var envelopeIds = envelopes
                .Where(envelope =>
                    envelope.Status == ActionExecutionStatus.Pending
                    || envelope.Status == ActionExecutionStatus.Failed)
                .Select(envelope => envelope.Id)
                .ToList();


            if (envelopeIds.Count == 0)
            {
                break;
            }

            foreach (var envelopeId in envelopeIds)
            {
                try
                {
                    await orchestrator.OrchestrateAsync(envelopeId.ToString(), CancellationToken.None);
                }
                catch
                {
                    // Suppress errors to allow tests to continue
                    // (mimics Hangfire behavior where job failures don't stop other processing)
                }
            }
        }
    }

    // ============================================================================
    // Reliability Test Helpers for Idempotency and Repeated Requests
    // ============================================================================

    /// <summary>
    /// Sets the Idempotency-Key header for the next request.
    /// </summary>
    protected void SetIdempotencyKey(string key)
    {
        Client.DefaultRequestHeaders.Remove("Idempotency-Key");
        Client.DefaultRequestHeaders.Add("Idempotency-Key", key);
    }

    /// <summary>
    /// Clears the Idempotency-Key header.
    /// </summary>
    protected void ClearIdempotencyKey()
    {
        Client.DefaultRequestHeaders.Remove("Idempotency-Key");
    }

    /// <summary>
    /// Sends the same request twice with the given idempotency key.
    /// Returns (firstResponse, secondResponse) for comparison.
    /// </summary>
    protected async Task<(HttpResponseMessage first, HttpResponseMessage second)> SendRepeatedRequestAsync<T>(
        string requestUri,
        T payload,
        string idempotencyKey)
    {
        SetIdempotencyKey(idempotencyKey);

        // First request
        var firstResponse = await PostAsJsonWithApiOptionsAsync(requestUri, payload);

        // Second request with same key
        var secondResponse = await PostAsJsonWithApiOptionsAsync(requestUri, payload);

        ClearIdempotencyKey();

        return (firstResponse, secondResponse);
    }

    // ============================================================================
    // Durable State Assertion Helpers
    // ============================================================================

    /// <summary>
    /// Returns the count of CommandEnvelope records with the given CorrelationId.
    /// Used to verify uniqueness enforcement in reliability tests.
    /// </summary>
    protected async Task<int> CountCommandEnvelopesByCorrelationIdAsync(Id<CorrelationScope> correlationId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.CommandEnvelopes
            .Where(ce => ce.CorrelationId == correlationId)
            .CountAsync();
    }

    /// <summary>
    /// Asserts that exactly one CommandEnvelope exists for the given correlation ID.
    /// Useful for verifying duplicate protection at the durable-intent layer.
    /// </summary>
    protected async Task AssertCommandEnvelopeUniquenessAsync(Id<CorrelationScope> correlationId, string? message = null)
    {
        var count = await CountCommandEnvelopesByCorrelationIdAsync(correlationId);

        count.Should().Be(1,
            message ?? $"Expected exactly one CommandEnvelope for CorrelationId {correlationId}, but found {count}");
    }

    /// <summary>
    /// Returns the count of NotificationMessage records with the given (Type, CorrelationId, Recipient) tuple.
    /// Used to verify email/notification deduplication.
    /// </summary>
    protected async Task<int> CountNotificationMessagesByKeyAsync(
        EmailNotificationType notificationType,
        Id<CorrelationScope> correlationId,
        Email recipient)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.NotificationMessages
            .Where(nm => nm.Type == notificationType
                      && nm.CorrelationId == correlationId
                      && nm.Recipient == recipient)
            .CountAsync();
    }

    /// <summary>
    /// Asserts that exactly one NotificationMessage exists for the given key tuple.
    /// Useful for verifying duplicate protection at the email/notification layer.
    /// </summary>
    protected async Task AssertNotificationMessageUniquenessAsync(
        EmailNotificationType notificationType,
        Id<CorrelationScope> correlationId,
        Email recipient,
        string? message = null)
    {
        var count = await CountNotificationMessagesByKeyAsync(notificationType, correlationId, recipient);

        count.Should().Be(1,
            message ?? $"Expected exactly one NotificationMessage for Type={notificationType}, CorrelationId={correlationId}, Recipient={recipient}, but found {count}");
    }

    /// <summary>
    /// Gets all CommandEnvelope statuses for a correlation ID.
    /// Useful for inspecting the full state history in reliability tests.
    /// </summary>
    protected async Task<List<(Id<CommandEnvelope>, ActionExecutionStatus)>> GetCommandEnvelopeStatusesAsync(Id<CorrelationScope> correlationId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.CommandEnvelopes
            .Where(ce => ce.CorrelationId == correlationId)
            .Select(ce => new { ce.Id, ce.Status })
            .AsNoTracking()
            .ToListAsync()
            .ContinueWith(t => t.Result.Select(x => (x.Id, x.Status)).ToList());
    }

    /// <summary>
    /// Counts all CommandEnvelope records currently in the database.
    /// Useful for baseline checks and transaction isolation verification.
    /// </summary>
    protected async Task<int> CountAllCommandEnvelopesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.CommandEnvelopes.CountAsync();
    }

    /// <summary>
    /// Counts all NotificationMessage records currently in the database.
    /// Useful for baseline checks and email deduplication verification.
    /// </summary>
    protected async Task<int> CountAllNotificationMessagesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.NotificationMessages.CountAsync();
    }

}
