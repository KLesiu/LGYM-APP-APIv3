using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class ReportingEngineTests : IntegrationTestBase
{
    [Test]
    public async Task ReportFlow_TrainerCreatesRequest_TraineeSubmits_TrainerCanReadSubmission()
    {
        var trainer = await SeedTrainerAsync("trainer-reports", "trainer-reports@example.com");
        var trainee = await SeedUserAsync(name: "trainee-reports", email: "trainee-reports@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var createTemplateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Weekly Check-in",
            description = "Basic wellness report",
            fields = new object[]
            {
                new { key = "weight", label = "Weight", type = "Number", isRequired = true, order = 0 },
                new { key = "sleptWell", label = "Slept Well", type = "Boolean", isRequired = false, order = 1 }
            }
        });

        createTemplateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await createTemplateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        template.Should().NotBeNull();
        template!.Fields.Should().HaveCount(2);

        var createRequestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template.Id,
            dueAt = DateTimeOffset.UtcNow.AddDays(2)
        });

        createRequestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await createRequestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();
        request.Should().NotBeNull();
        request!.Status.Should().Be("Pending");

        SetAuthorizationHeader(trainee.Id);
        var pendingResponse = await Client.GetAsync("/api/trainee/report-requests");
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pending = await pendingResponse.Content.ReadFromJsonAsync<List<ReportRequestResponse>>();
        pending.Should().NotBeNull();
        pending!.Should().ContainSingle(x => x.Id == request.Id);

        var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request.Id}/submit", new
        {
            answers = new
            {
                weight = 81.2,
                sleptWell = true
            }
        });

        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        SetAuthorizationHeader(trainer.Id);
        var submissionsResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/report-submissions");
        submissionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissions = await submissionsResponse.Content.ReadFromJsonAsync<List<ReportSubmissionResponse>>();
        submissions.Should().NotBeNull();
        submissions!.Should().ContainSingle();
        submissions[0].ReportRequestId.Should().Be(request.Id);
        submissions[0].Answers["weight"].GetDouble().Should().BeApproximately(81.2, 0.001);
    }

    [Test]
    public async Task SubmitReport_WithInvalidDynamicFieldType_ReturnsBadRequest()
    {
        var trainer = await SeedTrainerAsync("trainer-reports-invalid", "trainer-reports-invalid@example.com");
        var trainee = await SeedUserAsync(name: "trainee-reports-invalid", email: "trainee-reports-invalid@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Daily",
            fields = new object[]
            {
                new { key = "mood", label = "Mood", type = "Text", isRequired = true, order = 0 }
            }
        });
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        SetAuthorizationHeader(trainee.Id);
        var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request!.Id}/submit", new
        {
            answers = new
            {
                mood = 123
            }
        });

        submitResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ReportRequests_WithMidnightDueAt_RemainVisibleUntilEndOfDay()
    {
        var trainer = await SeedTrainerAsync("trainer-reports-midnight", "trainer-reports-midnight@example.com");
        var trainee = await SeedUserAsync(name: "trainee-reports-midnight", email: "trainee-reports-midnight@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var createTemplateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Same-day deadline",
            fields = new object[]
            {
                new { key = "checkin", label = "Check-in", type = "Text", isRequired = true, order = 0 }
            }
        });

        createTemplateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await createTemplateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        template.Should().NotBeNull();

        var dueAt = DateTimeOffset.UtcNow.Date.AddDays(1);
        var createRequestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id,
            dueAt
        });

        createRequestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await createRequestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();
        request.Should().NotBeNull();
        request!.Status.Should().Be("Pending");

        SetAuthorizationHeader(trainee.Id);
        var pendingResponse = await Client.GetAsync("/api/trainee/report-requests");
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pending = await pendingResponse.Content.ReadFromJsonAsync<List<ReportRequestResponse>>();
        pending.Should().NotBeNull();
        pending!.Should().ContainSingle(x => x.Id == request.Id);
    }

    [Test]
    public async Task TrainerReportingController_InvalidIds_ReturnBadRequest()
    {
        var trainer = await SeedTrainerAsync("trainer-report-invalid-ids", "trainer-report-invalid-ids@example.com");
        SetAuthorizationHeader(trainer.Id);

        var getTemplate = await Client.GetAsync("/api/trainer/report-templates/not-a-guid");
        var updateTemplate = await Client.PostAsJsonAsync("/api/trainer/report-templates/not-a-guid/update", new
        {
            name = "x",
            fields = new object[] { new { key = "k", label = "l", type = "Text", isRequired = false, order = 0 } }
        });
        var deleteTemplate = await Client.PostAsync("/api/trainer/report-templates/not-a-guid/delete", content: null);
        var createRequestBadTrainee = await Client.PostAsJsonAsync("/api/trainer/trainees/not-a-guid/report-requests", new
        {
            templateId = Domain.ValueObjects.Id<object>.New().ToString()
        });
        var createRequestBadTemplate = await Client.PostAsJsonAsync($"/api/trainer/trainees/{Domain.ValueObjects.Id<User>.New()}/report-requests", new
        {
            templateId = "not-a-guid"
        });
        var getSubmissionsBadTrainee = await Client.GetAsync("/api/trainer/trainees/not-a-guid/report-submissions");

        getTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        updateTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        deleteTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        createRequestBadTrainee.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        createRequestBadTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        getSubmissionsBadTrainee.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TrainerReportingController_TemplateCreateAndReadFlow_Works()
    {
        var trainer = await SeedTrainerAsync("trainer-report-crud", "trainer-report-crud@example.com");
        SetAuthorizationHeader(trainer.Id);

        var createTemplateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Weekly CRUD",
            description = "v1",
            fields = new object[]
            {
                new { key = "weight", label = "Weight", type = "Number", isRequired = true, order = 0 }
            }
        });
        createTemplateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createTemplateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        created.Should().NotBeNull();

        var getAllResponse = await Client.GetAsync("/api/trainer/report-templates");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var allTemplates = await getAllResponse.Content.ReadFromJsonAsync<List<ReportTemplateResponse>>();
        allTemplates.Should().NotBeNull();
        allTemplates!.Any(x => x.Id == created!.Id).Should().BeTrue();

        var getOneResponse = await Client.GetAsync($"/api/trainer/report-templates/{created!.Id}");
        getOneResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResponse = await Client.PostAsync($"/api/trainer/report-templates/{created.Id}/delete", content: null);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<User> SeedTrainerAsync(string name, string email)
    {
        var trainer = await SeedUserAsync(name: name, email: email, password: "password123");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var alreadyLinked = await db.UserRoles.AnyAsync(ur => ur.UserId == (Domain.ValueObjects.Id<User>)trainer.Id && ur.RoleId == (Domain.ValueObjects.Id<Role>)RoleSeedDataConfiguration.TrainerRoleSeedId);
        if (!alreadyLinked)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = (Domain.ValueObjects.Id<User>)trainer.Id,
                RoleId = (Domain.ValueObjects.Id<Role>)RoleSeedDataConfiguration.TrainerRoleSeedId
            });
            await db.SaveChangesAsync();
        }

        return trainer;
    }

    [Test]
    public async Task SubmitReport_WithMissingRequiredPhotoViews_ShouldReturnUnprocessableEntity()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-blocking", "trainer-photo-blocking@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-blocking", email: "trainee-photo-blocking@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo Progress Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front", "SideLeft", "SideRight", "Back" }
                    }
                }
            }
        });

        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        requestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        SetAuthorizationHeader(trainee.Id);
        var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request!.Id}/submit", new
        {
            answers = new
            {
                photos = Array.Empty<string>()
            }
        });

        submitResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errorContent = await submitResponse.Content.ReadAsStringAsync();
        errorContent.Should().Contain("Missing required photo views");
    }

    [Test]
    public async Task SubmitReport_WithAllRequiredPhotoViews_ShouldSucceed()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-success", "trainer-photo-success@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-success", email: "trainee-photo-success@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo Progress Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front", "SideLeft", "SideRight", "Back" }
                    }
                }
            }
        });

        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        requestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Id<ReportRequest>.TryParse(request!.Id, out var requestId).Should().BeTrue();
            var traineeId = trainee.Id;

            db.Photos.AddRange(
                new Photo
                {
                    Id = Id<Photo>.New(),
                    ReportRequestId = requestId,
                    OwnerUserId = traineeId,
                    UploaderUserId = traineeId,
                    ViewType = Domain.Enums.PhotoViewType.Front.ToString(),
                    StorageKey = "photos/front.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 1024,
                    Checksum = "abc123"
                },
                new Photo
                {
                    Id = Id<Photo>.New(),
                    ReportRequestId = requestId,
                    OwnerUserId = traineeId,
                    UploaderUserId = traineeId,
                    ViewType = Domain.Enums.PhotoViewType.SideLeft.ToString(),
                    StorageKey = "photos/side-left.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 1024,
                    Checksum = "def456"
                },
                new Photo
                {
                    Id = Id<Photo>.New(),
                    ReportRequestId = requestId,
                    OwnerUserId = traineeId,
                    UploaderUserId = traineeId,
                    ViewType = Domain.Enums.PhotoViewType.SideRight.ToString(),
                    StorageKey = "photos/side-right.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 1024,
                    Checksum = "ghi789"
                },
                new Photo
                {
                    Id = Id<Photo>.New(),
                    ReportRequestId = requestId,
                    OwnerUserId = traineeId,
                    UploaderUserId = traineeId,
                    ViewType = Domain.Enums.PhotoViewType.Back.ToString(),
                    StorageKey = "photos/back.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 1024,
                    Checksum = "jkl012"
                });

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainee.Id);
        var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request!.Id}/submit", new
        {
            answers = new
            {
                photos = Array.Empty<string>()
            }
        });

        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task EndToEnd_PhotoUploadInitiateAndSubmit_Success()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-e2e", "trainer-photo-e2e@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-e2e", email: "trainee-photo-e2e@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = (Id<User>)trainer.Id,
                TraineeId = (Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo E2E Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front" }
                    }
                }
            }
        });

        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        requestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        SetAuthorizationHeader(trainee.Id);
        var initiateResponse = await Client.PostAsJsonAsync("/api/trainee/photos/initiate", new
        {
            reportRequestId = request!.Id,
            viewType = "Front",
            mimeType = "image/jpeg",
            sizeBytes = 1024000
        });

        initiateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initiateResult = await initiateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var storageKey = initiateResult.GetProperty("storageKey").GetString();
        storageKey.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task EndToEnd_UnauthorizedPhotoUpload_Denied()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-unauth", "trainer-photo-unauth@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-unauth", email: "trainee-photo-unauth@example.com", password: "password123");
        var otherUser = await SeedUserAsync(name: "other-user-photo", email: "other-user-photo@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = (Id<User>)trainer.Id,
                TraineeId = (Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo Unauth Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front" }
                    }
                }
            }
        });

        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        SetAuthorizationHeader(otherUser.Id);
        var initiateResponse = await Client.PostAsJsonAsync("/api/trainee/photos/initiate", new
        {
            reportRequestId = request!.Id,
            viewType = "Front",
            mimeType = "image/jpeg",
            sizeBytes = 1024000
        });

        initiateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task EndToEnd_DuplicatePhotoUpload_ReplacesOld()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-dup", "trainer-photo-dup@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-dup", email: "trainee-photo-dup@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = (Id<User>)trainer.Id,
                TraineeId = (Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo Duplicate Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front" }
                    }
                }
            }
        });

        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Id<ReportRequest>.TryParse(request!.Id, out var requestId).Should().BeTrue();
            var traineeId = trainee.Id;

            var oldPhoto = new Photo
            {
                Id = Id<Photo>.New(),
                ReportRequestId = requestId,
                OwnerUserId = traineeId,
                UploaderUserId = traineeId,
                ViewType = PhotoViewType.Front.ToString(),
                StorageKey = "photos/old-front.jpg",
                MimeType = "image/jpeg",
                SizeBytes = 1024,
                Checksum = "old-checksum",
                IsDeleted = false
            };

            db.Photos.Add(oldPhoto);
            await db.SaveChangesAsync();

            var newPhoto = new Photo
            {
                Id = Id<Photo>.New(),
                ReportRequestId = requestId,
                OwnerUserId = traineeId,
                UploaderUserId = traineeId,
                ViewType = PhotoViewType.Front.ToString(),
                StorageKey = "photos/new-front.jpg",
                MimeType = "image/jpeg",
                SizeBytes = 2048,
                Checksum = "new-checksum",
                IsDeleted = false
            };

            oldPhoto.IsDeleted = true;

            db.Photos.Add(newPhoto);
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Id<ReportRequest>.TryParse(request!.Id, out var requestId).Should().BeTrue();

            var photos = await db.Photos
                .Where(p => p.ReportRequestId == requestId && p.ViewType == PhotoViewType.Front.ToString())
                .ToListAsync();

            photos.Should().HaveCount(2);
            photos.Count(p => p.IsDeleted).Should().Be(1);
            photos.Count(p => !p.IsDeleted).Should().Be(1);
            photos.Single(p => !p.IsDeleted).Checksum.Should().Be("new-checksum");
        }
    }

    [Test]
    public async Task EndToEnd_MixedTemplate_PhotosAndScalarFields_ValidatesCorrectly()
    {
        var trainer = await SeedTrainerAsync("trainer-mixed", "trainer-mixed@example.com");
        var trainee = await SeedUserAsync(name: "trainee-mixed", email: "trainee-mixed@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = (Id<User>)trainer.Id,
                TraineeId = (Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Mixed Template Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front", "Side" }
                    }
                },
                new
                {
                    key = "weight",
                    label = "Current Weight",
                    type = "Number",
                    isRequired = true,
                    order = 1
                },
                new
                {
                    key = "notes",
                    label = "Notes",
                    type = "Text",
                    isRequired = false,
                    order = 2
                }
            }
        });

        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        template!.Fields.Should().HaveCount(3);
        template.Fields.Should().Contain(f => f.Key == "photos");
        template.Fields.Should().Contain(f => f.Key == "weight");
        template.Fields.Should().Contain(f => f.Key == "notes");
    }

    private sealed class ReportTemplateResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("fields")]
        public List<ReportTemplateFieldResponse> Fields { get; set; } = [];
    }

    private sealed class ReportTemplateFieldResponse
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;
    }

    private sealed class ReportRequestResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    private sealed class ReportSubmissionResponse
    {
        [JsonPropertyName("reportRequestId")]
        public string ReportRequestId { get; set; } = string.Empty;

        [JsonPropertyName("answers")]
        public Dictionary<string, JsonElement> Answers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
