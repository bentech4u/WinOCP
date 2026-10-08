using System.Text.Json;
namespace WinOCP;
public static class ProjectCatalog
{
    // The OpenShift projects endpoint supplies the caller's visible projects.
    // Apply only a UI filter here; permissions remain enforced by the server.
    public static string[] UserProjects(string response) {
        using var document = JsonDocument.Parse(response);
        return document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("metadata").GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && name != "openshift"
                && !name.StartsWith("openshift-", StringComparison.Ordinal)
                && !name.StartsWith("kube-", StringComparison.Ordinal))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
