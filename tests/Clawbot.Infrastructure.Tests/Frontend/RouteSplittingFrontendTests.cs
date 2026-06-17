using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Frontend;

public sealed class RouteSplittingFrontendTests
{
    [Fact]
    public void Frontend_routes_lazy_load_pages_behind_a_router_suspense_boundary()
    {
        var root = FindRepositoryRoot();
        var routes = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "app", "routes.tsx"));
        var lazyPages = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "app", "lazyPages.tsx"));
        var main = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "main.tsx"));

        routes.Should().Contain("from \"./lazyPages\"");
        routes.Should().NotContain("import { lazy } from \"react\"");
        routes.Should().NotContain("lazy(() => import(");

        lazyPages.Should().Contain("import { lazy } from \"react\"");
        lazyPages.Should().Contain("lazy(() => import(\"@/features/dashboard/DashboardPage\"))");
        lazyPages.Should().Contain("lazy(() => import(\"@/features/admin/AdminConsolePage\"))");
        lazyPages.Should().Contain("lazy(() => import(\"@/features/public/WidgetDemoPage\"))");
        routes.Should().NotContain("import DashboardPage from \"@/features/dashboard/DashboardPage\"");
        routes.Should().NotContain("import AdminConsolePage from \"@/features/admin/AdminConsolePage\"");

        main.Should().Contain("import { StrictMode, Suspense } from \"react\"");
        main.Should().Contain("<Suspense");
        main.Should().Contain("<RouterProvider router={router} />");
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
