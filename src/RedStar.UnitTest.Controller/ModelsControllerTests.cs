using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RedStar.Controller;
using RedStar.UnitTest.Controller.Fakes;

namespace RedStar.UnitTest.Controller;

public class ModelsControllerTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task GetModels_ReturnsGatewayResponseVerbatim()
    {
        var gateway = new FakeLmStudioGateway { Response = new LmStudioResponse(200, """{"models": []}""") };
        var controller = new global::RedStar.Controller.Controllers.ModelsController(gateway);

        var result = Assert.IsType<ContentResult>(await controller.GetModels(CancellationToken.None));

        Assert.Equal("GetModelsAsync", gateway.LastMethod);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("""{"models": []}""", result.Content);
        Assert.Equal("application/json", result.ContentType);
    }

    [Fact]
    public async Task LoadModel_ForwardsRequestBodyToGateway_AndReturnsGatewayResponseVerbatim()
    {
        var gateway = new FakeLmStudioGateway { Response = new LmStudioResponse(200, """{"status": "loaded"}""") };
        var controller = new global::RedStar.Controller.Controllers.ModelsController(gateway);
        var requestJson = """{"model": "openai/gpt-oss-20b"}""";

        var result = Assert.IsType<ContentResult>(await controller.LoadModel(Parse(requestJson), CancellationToken.None));

        Assert.Equal("LoadModelAsync", gateway.LastMethod);
        Assert.Equal(requestJson, gateway.LastArgument);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("""{"status": "loaded"}""", result.Content);
    }

    [Fact]
    public async Task UnloadModel_ForwardsRequestBodyToGateway_AndReturnsGatewayResponseVerbatim()
    {
        var gateway = new FakeLmStudioGateway { Response = new LmStudioResponse(200, """{"instance_id": "x"}""") };
        var controller = new global::RedStar.Controller.Controllers.ModelsController(gateway);
        var requestJson = """{"instance_id": "x"}""";

        var result = Assert.IsType<ContentResult>(await controller.UnloadModel(Parse(requestJson), CancellationToken.None));

        Assert.Equal("UnloadModelAsync", gateway.LastMethod);
        Assert.Equal(requestJson, gateway.LastArgument);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task DownloadModel_ForwardsRequestBodyToGateway_AndReturnsGatewayResponseVerbatim()
    {
        var gateway = new FakeLmStudioGateway { Response = new LmStudioResponse(200, """{"job_id": "job_1"}""") };
        var controller = new global::RedStar.Controller.Controllers.ModelsController(gateway);
        var requestJson = """{"model": "ibm/granite-4-micro"}""";

        var result = Assert.IsType<ContentResult>(await controller.DownloadModel(Parse(requestJson), CancellationToken.None));

        Assert.Equal("DownloadModelAsync", gateway.LastMethod);
        Assert.Equal(requestJson, gateway.LastArgument);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task GetDownloadStatus_ForwardsJobIdToGateway_AndReturnsGatewayResponseVerbatim()
    {
        var gateway = new FakeLmStudioGateway { Response = new LmStudioResponse(200, """{"status": "downloading"}""") };
        var controller = new global::RedStar.Controller.Controllers.ModelsController(gateway);

        var result = Assert.IsType<ContentResult>(await controller.GetDownloadStatus("job_493c7c9ded", CancellationToken.None));

        Assert.Equal("GetDownloadStatusAsync", gateway.LastMethod);
        Assert.Equal("job_493c7c9ded", gateway.LastArgument);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task GetModels_ReturnsNonSuccessStatusCodeFromGateway_Verbatim()
    {
        var gateway = new FakeLmStudioGateway { Response = new LmStudioResponse(503, """{"error": "no models loaded"}""") };
        var controller = new global::RedStar.Controller.Controllers.ModelsController(gateway);

        var result = Assert.IsType<ContentResult>(await controller.GetModels(CancellationToken.None));

        Assert.Equal(503, result.StatusCode);
        Assert.Equal("""{"error": "no models loaded"}""", result.Content);
    }
}
