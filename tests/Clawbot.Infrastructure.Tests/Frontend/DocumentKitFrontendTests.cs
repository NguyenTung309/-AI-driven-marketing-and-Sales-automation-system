using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Frontend;

public sealed class DocumentKitFrontendTests
{
    [Fact]
    public void Documents_workspace_exposes_onboarding_brochure_slide_kit_generation()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "shared", "api", "documents.ts"));
        var page = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "features", "documents", "DocumentsPage.tsx"));
        var contracts = File.ReadAllText(Path.Combine(root, "src", "api", "Clawbot.Api.Contracts", "Documents", "DocumentsDtos.cs"));
        var endpoint = File.ReadAllText(Path.Combine(root, "src", "api", "Clawbot.Api", "Endpoints", "DocumentsEndpoints.cs"));

        contracts.Should().Contain("GenerateDocumentKitRequest");
        contracts.Should().Contain("GenerateDocumentKitResponse");
        endpoint.Should().Contain("generate-kit");
        endpoint.Should().Contain("ONBOARDING-KIT");
        endpoint.Should().Contain("BROCHURE-HSK");
        endpoint.Should().Contain("SLIDE-DEMO-5");

        api.Should().Contain("generateDocumentKit");
        api.Should().Contain("/api/docs/generate-kit");
        page.Should().Contain("Tạo bộ tài liệu");
        page.Should().Contain("generateKitMutation");
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
