using System.Net;
using System.Text;
using Application.Abstractions.Services;
using Infrastructure.FiscalLink;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.FiscalLink;

public sealed class FiscalLinkAccountingTests
{
    private const string Receipt = """
        {"commandId":"receipt-1","commandType":"cashRegister.printFiscalReceipt","serialNumber":"DT123456",
         "status":"Succeeded","statusCode":200,"completedAt":"2026-10-10T12:00:00Z","body":{"receiptNumber":"128","total":47.5}}
        """;

    private const string Report = """
        {"commandId":"z-1","commandType":"cashRegister.printReportZ","serialNumber":"DT123456",
         "status":"Succeeded","statusCode":200,"completedAt":"2026-10-10T20:00:00Z",
         "body":{"Data":{"reportNumber":125,"taxGroups":{"A":"47.50"},"payments":{"payment_method1":"47.50","payment_method2":0}}}}
        """;

    [Fact]
    public async Task ReadsSuccessfulCashDocumentsWithoutSendingAnyPrinterCommand()
    {
        using var recorder = new Responses($$"""{"items":[{{Receipt}},{{Report}}],"totalCount":2,"pageSize":100}""");
        using var http = new HttpClient(recorder);
        FiscalLinkCashDocuments documents = (await Reader(http).ReadAsync(Guid.NewGuid())).Value;

        documents.Receipts.Single().Total.ShouldBe(47.5m);
        documents.Reports.Single().Total.ShouldBe(47.5m);
        documents.Reports.Single().Number.ShouldBe("125");
        documents.Notes.ShouldBeEmpty();
        recorder.Requests.Single().Method.ShouldBe(HttpMethod.Get);
        recorder.Requests.Single().Path.ShouldContain("cash-registers/DT123456/commands?page=1&pageSize=100");
        recorder.Requests.Single().Key.ShouldBe("fsk_test_commands");
    }

    [Fact]
    public async Task ReadsEveryPageAndFetchesMissingCommandBodies()
    {
        string summary = """{"commandId":"receipt-1","commandType":"cashRegister.printFiscalReceipt","serialNumber":"DT123456","status":"Succeeded"}""";
        using var recorder = new Responses(
            $$"""{"items":[{{summary}}],"total":2,"pageSize":1}""", Receipt,
            $$"""{"items":[{{Report}}],"total":2,"pageSize":1}""");
        using var http = new HttpClient(recorder);
        FiscalLinkCashDocuments documents = (await Reader(http).ReadAsync(Guid.NewGuid())).Value;
        documents.Receipts.Count.ShouldBe(1);
        documents.Reports.Count.ShouldBe(1);
        recorder.Requests[1].Path.ShouldEndWith("commands/receipt-1");
        recorder.Requests[2].Path.ShouldContain("page=2");
        recorder.Requests.All(r => r.Method == HttpMethod.Get).ShouldBeTrue();
    }

    [Fact]
    public async Task FailedXAndNonFiscalCommandsCannotCreateAccountingDocuments()
    {
        using var recorder = new Responses($$"""
            {"items":[
            {{Receipt.Replace("Succeeded", "Failed", StringComparison.Ordinal)}},
            {{Report.Replace("printReportZ", "printReportX", StringComparison.Ordinal)}},
            {{Receipt.Replace("printFiscalReceipt", "printNonFiscalReceipt", StringComparison.Ordinal)}}],"total":3}
            """);
        using var http = new HttpClient(recorder);
        FiscalLinkCashDocuments documents = (await Reader(http).ReadAsync(Guid.NewGuid())).Value;
        documents.Receipts.ShouldBeEmpty();
        documents.Reports.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\"total\":47.5", "\"total\":\"47,50\"")]
    [InlineData("\"receiptNumber\":\"128\"", "\"receiptNumber\":null")]
    [InlineData("DT123456", "OTHER_REGISTER")]
    [InlineData("\"completedAt\":\"2026-10-10T12:00:00Z\"", "\"completedAt\":null")]
    public async Task InvalidOrForeignFinancialDataIsFlaggedInsteadOfGuessed(string original, string replacement)
    {
        using var recorder = new Responses($$"""{"items":[{{Receipt.Replace(original, replacement, StringComparison.Ordinal)}}],"total":1}""");
        using var http = new HttpClient(recorder);
        FiscalLinkCashDocuments documents = (await Reader(http).ReadAsync(Guid.NewGuid())).Value;
        documents.Receipts.ShouldBeEmpty();
        documents.Notes.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AnUnexpectedCardTotalIsNotRecordedAsCash()
    {
        using var recorder = new Responses($$"""{"items":[{{Report.Replace("\"payment_method2\":0", "\"payment_method2\":10", StringComparison.Ordinal)}}],"total":1}""");
        using var http = new HttpClient(recorder);
        FiscalLinkCashDocuments documents = (await Reader(http).ReadAsync(Guid.NewGuid())).Value;
        documents.Reports.ShouldBeEmpty();
        documents.Notes.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AFailedPageDoesNotReturnAnApparentlyCompletePartialBatch()
    {
        using var recorder = new Responses($$"""{"items":[{{Receipt}}],"total":2,"pageSize":1}""", "denied") { FailLast = true };
        using var http = new HttpClient(recorder);
        Result<FiscalLinkCashDocuments> result = await Reader(http).ReadAsync(Guid.NewGuid());
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FiscalLink.AccountingReadFailed");
    }

    [Fact]
    public async Task MissingCommandKeyDoesNotMakeARequest()
    {
        using var recorder = new Responses("{}");
        using var http = new HttpClient(recorder);
        Result<FiscalLinkCashDocuments> result = await Reader(http, commandKey: null).ReadAsync(Guid.NewGuid());
        result.Error.Code.ShouldBe("FiscalLink.AccountingNotConfigured");
        recorder.Requests.ShouldBeEmpty();
    }

    private static FiscalLinkAccountingService Reader(HttpClient http, string? commandKey = "fsk_test_commands") => new(http, new Registers(),
        Options.Create(new FiscalLinkOptions { ManagementKey = "fsk_test_management", CommandKey = commandKey }), NullLogger<FiscalLinkAccountingService>.Instance);

    // Activarea casei e o dată fixă, înaintea documentelor din test: cu „acum”, documentele deveneau
    // „mai vechi decât activarea” a doua zi și testele picau singure.
    private static readonly DateTime Activated = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Registers : IFiscalLinkService
    {
        public Task<Result<Guid>> CreateClientAsync(FiscalLinkNewClient client, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FiscalLinkActivation>> GetActivationAsync(Guid clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<FiscalLinkRegister>>> ListRegistersAsync(Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<FiscalLinkRegister>>([new FiscalLinkRegister(Guid.NewGuid(), "DT123456", "Active", true, false, Activated)]));
    }

    private sealed class Responses(params string[] bodies) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Key)> Requests { get; } = [];
        public bool FailLast { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.GetValues("X-Api-Key").Single()));
            int index = Requests.Count - 1;
            return Task.FromResult(new HttpResponseMessage(FailLast && index == bodies.Length - 1 ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
                { Content = new StringContent(bodies[index], Encoding.UTF8, "application/json") });
        }
    }
}
