using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Frontend;

public sealed class PublicWidgetFrontendTests
{
    [Fact]
    public void Public_widget_and_support_pages_keep_s17_routes_api_and_stitch_default_branding()
    {
        var root = FindRepositoryRoot();
        var routes = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "app", "routes.tsx"));
        var publicApi = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "shared", "api", "publicWidget.ts"));
        var widgetPage = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "features", "public", "WidgetDemoPage.tsx"));
        var supportPage = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "features", "public", "SupportFaqPage.tsx"));

        routes.Should().Contain("{ path: \"/chat-widget\", element: <WidgetDemoPage /> }");
        routes.Should().Contain("{ path: \"/chat-widget/:tenantSlug\", element: <WidgetDemoPage /> }");
        routes.Should().Contain("{ path: \"/support\", element: <SupportFaqPage /> }");
        routes.Should().Contain("{ path: \"/support/:tenantSlug\", element: <SupportFaqPage /> }");

        var routerStart = routes.IndexOf("export const router = createBrowserRouter([", StringComparison.Ordinal);
        var firstProtectedRoute = routes.IndexOf("path: \"/\",", routerStart, StringComparison.Ordinal);
        var publicRouteBlock = routes[routerStart..firstProtectedRoute];
        publicRouteBlock.Should().NotContain("RequireAuth");

        publicApi.Should().Contain("/api/public/widget/${encodeURIComponent(tenantSlug)}/bootstrap");
        publicApi.Should().Contain("/api/public/widget/${encodeURIComponent(tenantSlug)}/faq");
        publicApi.Should().Contain("/api/public/widget/${encodeURIComponent(tenantSlug)}/lead");
        publicApi.Should().Contain("/api/public/widget/${encodeURIComponent(tenantSlug)}/messages");

        widgetPage.Should().Contain("primaryColor: \"#d32f2f\"");
        supportPage.Should().Contain("primaryColor: \"#d32f2f\"");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Clawbot.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
