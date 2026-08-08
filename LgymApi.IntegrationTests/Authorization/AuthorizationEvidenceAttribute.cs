namespace LgymApi.IntegrationTests.Authorization;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class AuthorizationEvidenceAttribute(
    string method,
    string route,
    string accessClass,
    string category) : Attribute
{
    public string Method { get; } = method;

    public string Route { get; } = route;

    public string AccessClass { get; } = accessClass;

    public string Category { get; } = category;
}
