using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.Portfolio.Contracts;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>Portfolio CRUD end to end, over real HTTP against a real Postgres.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class HoldingsTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>A new position is 201 and addressable.</summary>
    [Fact]
    public async Task AddHolding_ReturnsCreated_WithLocationHeader()
    {
        var (client, token) = await SignedInAsync("holdings-create");

        using var response = await Wire.AddHoldingAsync(client, token, "AAPL", 10m, 100m);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(response));
        response.Headers.Location!.ToString().ShouldStartWith("/api/holdings/");
    }

    /// <summary>The canonical case of the whole phase: 10 @ 100 then 10 @ 150 is one row, 20 @ 125.</summary>
    [Fact]
    public async Task AddHolding_SameTickerTwice_Returns200Merged_OneRowInDatabase()
    {
        var (client, token) = await SignedInAsync("holdings-merge");

        using var first = await Wire.AddHoldingAsync(client, token, "AAPL", 10m, 100m);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(first));

        // Lower case on purpose: Ticker.Create normalises, so this must find the same row.
        using var second = await Wire.AddHoldingAsync(client, token, "aapl", 10m, 150m);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(second));

        var holdings = await Wire.ListHoldingsAsync(client, token);

        var only = holdings.ShouldHaveSingleItem();
        only.Ticker.ShouldBe("AAPL");
        only.Quantity.ShouldBe(20m);

        // Parsed, not string-compared: decimal preserves scale, so the serialised form is legitimately
        // either "125" or "125.000000" depending on what the division and the column produced. Asserting
        // the string here would be asserting an implementation detail of decimal arithmetic.
        Amount(only.AveragePrice).ShouldBe(125m);
        Amount(only.Invested).ShouldBe(2500m);
    }

    /// <summary>The string form is the wire contract, so it is asserted directly and exactly once.</summary>
    [Fact]
    public async Task Holdings_SerialiseMoneyAsAString_NotANumber()
    {
        var (client, token) = await SignedInAsync("holdings-money-shape");

        using var added = await Wire.AddHoldingAsync(client, token, "IBM", 2m, 50m);
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(added));

        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/holdings", token);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldContain(
            "\"amount\":\"",
            Case.Sensitive,
            "MoneyJsonConverter must emit a quoted amount; an unquoted one means it is not registered.");
    }

    /// <summary>Shape validation rejects a symbol that is not one, before any handler runs.</summary>
    [Theory]
    [InlineData("TOOLONG")]
    [InlineData("BRK.B")]
    [InlineData("")]
    public async Task AddHolding_MalformedTicker_Returns400(string ticker)
    {
        var (client, token) = await SignedInAsync("holdings-bad-ticker");

        using var response = await Wire.AddHoldingAsync(client, token, ticker, 10m, 100m);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Wire.Describe(response));
        response.Content.Headers.ContentType!.MediaType.ShouldBe(Wire.ProblemJson);
    }

    /// <summary>Three bad fields, one 400 naming all three — which only the filter can do, since a handler returns one InvalidInput.</summary>
    [Fact]
    public async Task AddHolding_TickerQuantityAndPriceAllInvalid_NamesEveryFailingField()
    {
        var (client, token) = await SignedInAsync("holdings-add-every-field");

        using var response = await Wire.AddHoldingAsync(client, token, "TOOLONG", 0m, 0m);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Wire.Describe(response));
        response.Content.Headers.ContentType!.MediaType.ShouldBe(Wire.ProblemJson);

        var fields = await Wire.FailingFieldsAsync(response);

        // Remove AddEndpointFilter<ValidationFilter<AddHoldingRequest>> and this is still a 400 — the
        // handler's UnknownTicker becomes one — but it can only ever name one field. That is the gap.
        fields.SetEquals(["ticker", "quantity", "price"]).ShouldBeTrue(
            $"the 400 named [{string.Join(", ", fields.Order(StringComparer.Ordinal))}]. All three fields "
            + "are invalid, and only ValidationFilter<AddHoldingRequest> reports them together: a handler "
            + "returns a single InvalidInput, so one name here means the filter is no longer in the pipeline.");
    }

    /// <summary>The PATCH validator is otherwise never exercised in place, because nothing else sends an invalid PATCH.</summary>
    [Fact]
    public async Task UpdateHolding_QuantityAndPriceBothInvalid_NamesBothFailingFields()
    {
        var (client, token) = await SignedInAsync("holdings-patch-every-field");

        await AddSucceedsAsync(client, token, "INTC", 5m, 30m);

        var id = (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem().Id;

        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/holdings/{id}",
            token,
            new { quantity = 0m, price = 0m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Wire.Describe(response));
        response.Content.Headers.ContentType!.MediaType.ShouldBe(Wire.ProblemJson);

        var fields = await Wire.FailingFieldsAsync(response);

        fields.SetEquals(["quantity", "price"]).ShouldBeTrue(
            $"the 400 named [{string.Join(", ", fields.Order(StringComparer.Ordinal))}]. Without "
            + "ValidationFilter<UpdateHoldingRequest> the request reaches Holding.Correct, which returns "
            + "one InvalidInput and therefore names quantity alone.");

        // The 400 refused before writing: a rejection that had already changed the row would be worse.
        (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem().Quantity.ShouldBe(5m);
    }

    /// <summary>A correction replaces the position; it is not a second purchase and must never average.</summary>
    [Fact]
    public async Task UpdateHolding_ChangesQuantityAndPrice_AndDoesNotAverage()
    {
        var (client, token) = await SignedInAsync("holdings-update");

        await AddSucceedsAsync(client, token, "MSFT", 10m, 100m);
        await AddSucceedsAsync(client, token, "MSFT", 10m, 150m);          // now 20 @ 125

        var id = (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem().Id;

        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/holdings/{id}",
            token,
            new { quantity = 15m, price = 120m });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var corrected = (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem();
        corrected.Quantity.ShouldBe(15m);

        // 120 and not 122.5: averaging the correction into the existing 125 is the bug this pins.
        Amount(corrected.AveragePrice).ShouldBe(120m, "Correct replaces; it must never average");
        Amount(corrected.Invested).ShouldBe(1800m);
    }

    /// <summary>The 201 body and the GET after it must be the same numbers, or the screen changes by itself.</summary>
    [Fact]
    public async Task AddHolding_ValuesFinerThanTheColumn_ReadBackExactlyAsTheyWereReturned()
    {
        var (client, token) = await SignedInAsync("holdings-round-trip");

        // A seventh decimal of exactly 5 is the case that separates the two rounding rules, so if the
        // domain does not round and the column is left to do it, the 201 body and the row disagree.
        using var created = await Wire.AddHoldingAsync(client, token, "NFLX", 2.0000005m, 1.0000005m);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(created));

        var posted = await created.Content.ReadFromJsonAsync<HoldingPayload>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        posted.ShouldNotBeNull();

        var fetched = (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem();

        Amount(posted.AveragePrice).ShouldBe(
            Amount(fetched.AveragePrice),
            "the price the POST reported is not the price the GET returns, so the number the user was "
            + "just shown changed on refresh");

        posted.Quantity.ShouldBe(fetched.Quantity, "the quantity changed between the POST and the GET");

        // Which way they agreed, so a future regression that rounds both ends wrongly still shows up.
        Amount(fetched.AveragePrice).ShouldBe(1.000000m);
        fetched.Quantity.ShouldBe(2m);
    }

    /// <summary>Correct writes through the same column, so it needs the same round-trip proof.</summary>
    [Fact]
    public async Task UpdateHolding_ValuesFinerThanTheColumn_ReadBackExactlyAsTheyWereReturned()
    {
        var (client, token) = await SignedInAsync("holdings-round-trip-patch");

        await AddSucceedsAsync(client, token, "SBUX", 4m, 80m);

        var id = (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem().Id;

        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/holdings/{id}",
            token,
            new { quantity = 2.0000005m, price = 1.0000005m });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var patched = await response.Content.ReadFromJsonAsync<HoldingPayload>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        patched.ShouldNotBeNull();

        var fetched = (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem();

        Amount(patched.AveragePrice).ShouldBe(Amount(fetched.AveragePrice));
        patched.Quantity.ShouldBe(fetched.Quantity);

        Amount(fetched.AveragePrice).ShouldBe(1.000000m);
        fetched.Quantity.ShouldBe(2m);
    }

    /// <summary>A quantity numeric(18,6) cannot hold is a 400 naming the field, never a 22003 as a bare 500.</summary>
    [Fact]
    public async Task AddHolding_QuantityTooLargeForTheColumn_Returns400_NotAnOverflow500()
    {
        var (client, token) = await SignedInAsync("holdings-overflow");

        using var response = await Wire.AddHoldingAsync(client, token, "AAPL", 1_000_000_000_000m, 100m);

        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest,
            await Wire.Describe(response));

        response.Content.Headers.ContentType!.MediaType.ShouldBe(Wire.ProblemJson);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("uantity", Case.Sensitive, "the 400 must say which field was wrong");
    }

    /// <summary>The security assertion of the phase: a 403 would confirm to a stranger that this id exists.</summary>
    [Fact]
    public async Task UpdateHolding_OtherUsersHolding_Returns404_NotForbidden()
    {
        var (ownerClient, ownerToken) = await SignedInAsync("holdings-owner");
        await AddSucceedsAsync(ownerClient, ownerToken, "TSLA", 5m, 200m);

        var id = (await Wire.ListHoldingsAsync(ownerClient, ownerToken)).ShouldHaveSingleItem().Id;

        var (strangerClient, strangerToken) = await SignedInAsync("holdings-stranger");

        using var response = await Wire.SendAsync(
            strangerClient,
            HttpMethod.Patch,
            $"/api/holdings/{id}",
            strangerToken,
            new { quantity = 1m, price = 1m });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await Wire.Describe(response));

        // The owner's row is untouched, so the 404 was a refusal rather than a silent success.
        var untouched = (await Wire.ListHoldingsAsync(ownerClient, ownerToken)).ShouldHaveSingleItem();
        untouched.Quantity.ShouldBe(5m);
    }

    /// <summary>The same control on the delete route, which has its own repository call.</summary>
    [Fact]
    public async Task RemoveHolding_OtherUsersHolding_Returns404_AndLeavesItThere()
    {
        var (ownerClient, ownerToken) = await SignedInAsync("holdings-delete-owner");
        await AddSucceedsAsync(ownerClient, ownerToken, "ORCL", 7m, 90m);

        var id = (await Wire.ListHoldingsAsync(ownerClient, ownerToken)).ShouldHaveSingleItem().Id;

        var (strangerClient, strangerToken) = await SignedInAsync("holdings-delete-stranger");

        using var response = await Wire.SendAsync(
            strangerClient,
            HttpMethod.Delete,
            $"/api/holdings/{id}",
            strangerToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await Wire.Describe(response));

        (await Wire.ListHoldingsAsync(ownerClient, ownerToken)).ShouldHaveSingleItem();
    }

    /// <summary>Closing a position removes it.</summary>
    [Fact]
    public async Task RemoveHolding_Returns204_ThenGetIsEmpty()
    {
        var (client, token) = await SignedInAsync("holdings-remove");

        await AddSucceedsAsync(client, token, "NVDA", 3m, 400m);

        var id = (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem().Id;

        using var removed = await Wire.SendAsync(client, HttpMethod.Delete, $"/api/holdings/{id}", token);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await Wire.Describe(removed));

        (await Wire.ListHoldingsAsync(client, token)).ShouldBeEmpty();
    }

    /// <summary>A second delete of the same id is a 404, not a cheerful second 204.</summary>
    [Fact]
    public async Task RemoveHolding_Twice_SecondReturns404()
    {
        var (client, token) = await SignedInAsync("holdings-remove-twice");

        await AddSucceedsAsync(client, token, "AMD", 2m, 90m);

        var id = (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem().Id;

        using var first = await Wire.SendAsync(client, HttpMethod.Delete, $"/api/holdings/{id}", token);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent, await Wire.Describe(first));

        using var second = await Wire.SendAsync(client, HttpMethod.Delete, $"/api/holdings/{id}", token);
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound, await Wire.Describe(second));
    }

    /// <summary>The list is scoped to the caller, and a fresh account starting non-empty would be the leak.</summary>
    [Fact]
    public async Task GetHoldings_ShowsOnlyTheCallersPositions()
    {
        var (aliceClient, aliceToken) = await SignedInAsync("holdings-alice");
        await AddSucceedsAsync(aliceClient, aliceToken, "AAPL", 1m, 100m);

        var (bobClient, bobToken) = await SignedInAsync("holdings-bob");

        (await Wire.ListHoldingsAsync(bobClient, bobToken)).ShouldBeEmpty();

        // Alice still sees hers, so the emptiness above is scoping rather than a broken read.
        (await Wire.ListHoldingsAsync(aliceClient, aliceToken)).ShouldHaveSingleItem();
    }

    /// <summary>The unique index is the guarantee, not the handler's look-up. See §2.6.</summary>
    [Fact]
    public async Task AddHolding_ConcurrentSameTicker_OneRowSurvives()
    {
        var (client, token) = await SignedInAsync("holdings-race");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Wire.AddHoldingAsync(client, token, "AAPL", 10m, 100m)));

        try
        {
            // Deliberately not asserting the losers' status: under §2.6 a loser is a 500, and pinning
            // that would pin an accident. What ix_holdings_user_id_ticker guarantees is the row count.
            responses.ShouldContain(
                response => response.IsSuccessStatusCode,
                "at least one of four concurrent purchases must land");

            (await Wire.ListHoldingsAsync(client, token))
                .Count(holding => string.Equals(holding.Ticker, "AAPL", StringComparison.Ordinal))
                .ShouldBe(1, "ix_holdings_user_id_ticker is what makes a duplicate row impossible");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>The one thing Portfolio.Contracts exports, resolved from the host and answered both ways.</summary>
    [Fact]
    public async Task UserHoldsTicker_IsTrueForAPositionJustOpened_AndFalseForOneNeverBought()
    {
        var (client, token) = await SignedInAsync("holdings-contract");

        await AddSucceedsAsync(client, token, "GOOG", 1m, 100m);

        var userId = await SubjectOfAsync(client, token);

        using var scope = _fixture.Services.CreateScope();
        var holds = scope.ServiceProvider.GetRequiredService<IUserHoldsTicker>();

        (await holds.HoldsAsync(userId, "GOOG", TestContext.Current.CancellationToken)).ShouldBeTrue(
            "Alerts rejects a subscription on a ticker you do not hold, so a false here silently blocks "
            + "every alert the user could legitimately set.");

        // The negative case is what makes the positive one mean anything.
        (await holds.HoldsAsync(userId, "META", TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>Presses the button on ParameterisationTests: its assembly-wide claim only covers a context the recorder reached.</summary>
    [Fact]
    public async Task PortfolioContext_IsRecorded_SoTheParameterisationProofCoversHoldingsToo()
    {
        var (client, token) = await SignedInAsync("holdings-recorded");

        var before = _fixture.RecordedCommands.Commands.Count;

        await AddSucceedsAsync(client, token, "COST", 4m, 25m);

        var thisRequest = _fixture.RecordedCommands.Commands.Skip(before).ToArray();

        // Without AddToPortfolio nothing here is recorded, every other test still passes, and req 6's
        // evidence silently stops covering the second module. Nothing else in the suite notices.
        thisRequest.ShouldContain(
            command => command.CommandText.Contains("portfolio.holdings", StringComparison.OrdinalIgnoreCase),
            "the recording interceptor captured no SQL against portfolio.holdings for a request that "
            + "demonstrably wrote a row; ModuleDbContextInterceptors.AddToPortfolio is not attached.");

        var carriers = thisRequest
            .Where(command => command.ParameterValues.Any(
                value => value.Contains("COST", StringComparison.Ordinal)))
            .ToArray();

        carriers.ShouldNotBeEmpty("the ticker never travelled as a parameter, so nothing was proved.");

        foreach (var command in thisRequest)
        {
            command.CommandText.ShouldNotContain(
                "COST",
                Case.Sensitive,
                $"a user-supplied ticker was concatenated into SQL: {command.CommandText}");
        }
    }

    /// <summary>Parses the string amount back to a decimal. That it IS a string is asserted separately.</summary>
    private static decimal Amount(MoneyPayload payload) =>
        decimal.Parse(payload.Amount, CultureInfo.InvariantCulture);

    /// <summary>Reads the caller's own id off the running host.</summary>
    private static async Task<Guid> SubjectOfAsync(HttpClient client, string accessToken)
    {
        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var user = await response.Content.ReadFromJsonAsync<UserPayload>(JsonSerializerOptions.Web);

        user.ShouldNotBeNull();

        // Portfolio stores the owner as a uuid, so the parse is the same one its endpoints do.
        return user.Id;
    }

    private static async Task AddSucceedsAsync(
        HttpClient client,
        string accessToken,
        string ticker,
        decimal quantity,
        decimal price)
    {
        using var response = await Wire.AddHoldingAsync(client, accessToken, ticker, quantity, price);

        response.IsSuccessStatusCode.ShouldBeTrue(await Wire.Describe(response));
    }

    private async Task<(HttpClient Client, string Token)> SignedInAsync(string prefix)
    {
        var client = _fixture.CreateClient();
        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix));

        return (client, tokens.AccessToken);
    }
}
