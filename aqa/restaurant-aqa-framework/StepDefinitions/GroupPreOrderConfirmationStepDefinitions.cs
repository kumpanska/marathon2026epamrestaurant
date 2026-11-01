using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Dtos.Dishes;
using Restaurant_Aqa_Framework.Api.Dtos.GroupReservations;
using Restaurant_Aqa_Framework.Api.Dtos.Orders;
using Restaurant_Aqa_Framework.Api.Dtos.Reservations;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Api.Dtos.Tables;
using Restaurant_Aqa_Framework.Configuration;
using Restaurant_Aqa_Framework.Contexts;
using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Screenplay.Tasks;
using Restaurant_Aqa_Framework.Ui;
using Restaurant_Aqa_Framework.Ui.Components;

namespace Restaurant_Aqa_Framework.StepDefinitions;

[Binding]
public sealed class GroupPreOrderConfirmationStepDefinitions(
    Actor actor,
    ApiResponseContext response) : StepDefinitionBase(actor)
{
    private static readonly int[] ReservationCandidateHours = [11, 13, 15, 17, 19];

    [Given("the owner signs in through the interface")]
    public Task GivenOwnerSignsInThroughInterface()
    {
        var fixture = Recall<GroupReservationFixture>();
        return Actor.AttemptsToAsync(
            new SignInThroughTheLoginForm(fixture.OwnerEmail, fixture.OwnerPassword));
    }

    [Given("guest {string} adds {int} portions and submits through the invite page")]
    public Task GivenGuestAddsAndSubmits(string guestName, int quantity) =>
        AddGuestThroughInterfaceAsync(guestName, quantity, submit: true);

    [Given("guest {string} adds {int} portion and submits through the invite page")]
    public Task GivenGuestAddsAndSubmitsOne(string guestName, int quantity) =>
        AddGuestThroughInterfaceAsync(guestName, quantity, submit: true);

    [Given("guest {string} adds {int} portions through the invite page")]
    public Task GivenGuestAddsWithoutSubmitting(string guestName, int quantity) =>
        AddGuestThroughInterfaceAsync(guestName, quantity, submit: false);

    [Given("the owner's basket contains {int} confirmed portion")]
    public async Task GivenOwnerBasketContainsConfirmedDish(int quantity)
    {
        var fixture = Recall<GroupReservationFixture>();
        var groups = new GroupReservationsApi(Actor.UsingTheApi());
        await CaptureAndRequireSuccessAsync(
            await groups.GetOwnerGuestSessionAsync(fixture.Reservation.Id, fixture.OwnerToken),
            "Getting the owner's group session");
        var ownerSession = response.Json<OwnerGuestSessionResponse>();
        var dish = Recall<DishResponse>();
        await new GuestPreOrderPage(Actor.LookingAtThePage()).AddDishToExistingSessionAsync(
            fixture.InviteToken,
            "Owner",
            ownerSession.GuestSessionId,
            fixture.OwnerToken,
            dish.Name,
            quantity);
    }

    [Given("a guest has confirmed an empty basket through test setup")]
    public async Task GivenGuestConfirmedEmptyBasket()
    {
        var fixture = Recall<GroupReservationFixture>();
        var groups = new GroupReservationsApi(Actor.UsingTheApi());
        await CaptureAndRequireSuccessAsync(
            await groups.CreateGuestSessionAsync(
                new CreateGuestSessionRequest(fixture.GroupReservationId, SampleDataFactory.FirstName())),
            "Creating an empty guest session");
        var guest = response.Json<GuestSessionCreatedResponse>();
        await CaptureAndRequireSuccessAsync(
            await groups.SubmitGuestCartAsync(guest.SessionId, guest.SessionToken),
            "Confirming an empty guest basket");
    }

    [When("the owner opens the group pre-order from their reservation")]
    public async Task WhenOwnerOpensGroupPreOrder()
    {
        await new GroupOrderPage(Actor.LookingAtThePage()).OpenFromReservationsAsync();
    }

    [When("the owner submits the consolidated pre-order through the interface")]
    public async Task WhenOwnerSubmitsConsolidatedPreOrder()
    {
        var groupOrder = new GroupOrderPage(Actor.LookingAtThePage());
        await groupOrder.ConfirmAsync();
    }

    [When("the owner opens a group pre-order for a reservation that does not exist")]
    public async Task WhenOwnerOpensMissingReservationGroupOrder()
    {
        var groupOrder = new GroupOrderPage(Actor.LookingAtThePage());
        await groupOrder.NavigateToReservationAsync(Guid.NewGuid().ToString());
    }

    [When("the owner opens a reservation without a group pre-order")]
    public async Task WhenOwnerOpensReservationWithoutGroup()
    {
        string reservationId = await CreateReservationWithoutGroupAsync();
        await new GroupOrderPage(Actor.LookingAtThePage()).NavigateToReservationAsync(reservationId);
    }

    [When("another customer opens the group pre-order through the interface")]
    public async Task WhenAnotherCustomerOpensGroupPreOrder()
    {
        var page = Actor.LookingAtThePage();
        await new HeaderComponent(page).SignOutAsync();

        var visitor = SampleDataFactory.RegisterRequest();
        var auth = new AuthApi(Actor.UsingTheApi());
        await CaptureAndRequireSuccessAsync(
            await auth.RegisterAsync(visitor),
            "Registering a non-owner customer");
        await Actor.AttemptsToAsync(new SignInThroughTheLoginForm(visitor.Email, visitor.Password));

        var fixture = Recall<GroupReservationFixture>();
        await new GroupOrderPage(page).NavigateToReservationAsync(fixture.Reservation.Id);
    }

    [Then("the owner sees that the pre-order was sent to the restaurant")]
    public async Task ThenOwnerSeesPreOrderSent()
    {
        var page = Actor.LookingAtThePage();
        await Assert.That(await new GroupOrderPage(page).StatusBadge.InnerTextAsync())
            .IsEqualTo("Sent to Restaurant");
        await Assert.That(await new ToastComponent(page).Success.InnerTextAsync())
            .IsEqualTo("Pre-order sent to the restaurant");

        var dish = Recall<DishResponse>();
        var consolidatedDish = new GroupOrderPage(page).ConsolidatedDish(dish.Name);
        await Assert.That(await consolidatedDish.InnerTextAsync()).Contains("6");
    }

    [Then("the group booking and order are submitted")]
    public async Task ThenGroupBookingAndOrderAreSubmitted()
    {
        var fixture = Recall<GroupReservationFixture>();
        var groups = new GroupReservationsApi(Actor.UsingTheApi());
        await CaptureAndRequireSuccessAsync(
            await groups.GetGroupStatusAsync(fixture.GroupReservationId),
            "Checking the group booking status");
        await Assert.That(response.Json<GroupReservationStatusResponse>().Status).IsEqualTo("Submitted");

        var waiter = await Actor.AsksForAsync(new TheWaiterSession());
        await CaptureAndRequireSuccessAsync(
            await new OrdersApi(Actor.UsingTheApi()).GetOrderAsync(
                fixture.Reservation.Id,
                waiter.IdToken),
            "Checking the submitted order as the waiter");
        await Assert.That(response.Json<GetOrderResponse>().Status)
            .IsEqualTo(OrderStatus.SubmittedByCustomer);
    }

    [Then("the waiter sees {int} portions in the submitted order")]
    public async Task ThenWaiterSeesConsolidatedOrder(int expectedQuantity)
    {
        var fixture = Recall<GroupReservationFixture>();
        var waiter = await Actor.AsksForAsync(new TheWaiterSession());
        await CaptureAndRequireSuccessAsync(
            await new OrdersApi(Actor.UsingTheApi()).GetOrderAsync(fixture.Reservation.Id, waiter.IdToken),
            "Reading the submitted order as the waiter");

        var order = response.Json<GetOrderResponse>();
        var dish = Recall<DishResponse>();
        await Assert.That(order.Status).IsEqualTo(OrderStatus.SubmittedByCustomer);
        await Assert.That(order.OrderItems).Count().IsEqualTo(1);
        await Assert.That(order.OrderItems.Single().Dish.Id).IsEqualTo(dish.Id);
        await Assert.That(order.OrderItems.Single().Quantity).IsEqualTo(expectedQuantity);
    }

    [Then("the submit action is disabled with the no-dishes message")]
    public async Task ThenSubmitIsDisabledWhenNoDishes()
    {
        var groupOrder = new GroupOrderPage(Actor.LookingAtThePage());
        await Assert.That(await groupOrder.EmptyState.InnerTextAsync())
            .IsEqualTo("Participants haven't added any dishes yet");
        await Assert.That(await groupOrder.SubmitButton.IsDisabledAsync()).IsTrue();
    }

    [Then("the owner sees the error {string}")]
    public async Task ThenOwnerSeesError(string message)
    {
        var error = new ToastComponent(Actor.LookingAtThePage()).Error;
        await error.WaitForAsync();
        await Assert.That(await error.InnerTextAsync()).IsEqualTo(message);
    }

    [Then("the group pre-order page reports {string}")]
    public async Task ThenGroupOrderPageReportsError(string message)
    {
        var page = Actor.LookingAtThePage();
        var errorToast = new ToastComponent(page).Error;
        await errorToast.WaitForAsync();
        await Assert.That(await errorToast.InnerTextAsync()).IsEqualTo(message);

        var heading = new GroupOrderPage(page).ErrorHeading;
        await Assert.That(await heading.InnerTextAsync())
            .IsEqualTo("Couldn't load the group reservation.");
    }

    [Then("the submit action is unavailable and the status is {string}")]
    public async Task ThenSubmitIsUnavailable(string status)
    {
        var page = Actor.LookingAtThePage();
        await Assert.That(await new GroupOrderPage(page).StatusBadge.InnerTextAsync()).IsEqualTo(status);
        await Assert.That(await new GroupOrderPage(page).SubmitButton.CountAsync()).IsEqualTo(0);
    }

    private async Task AddGuestThroughInterfaceAsync(string name, int quantity, bool submit)
    {
        var fixture = Recall<GroupReservationFixture>();
        var dish = Recall<DishResponse>();
        var page = new GuestPreOrderPage(Actor.LookingAtThePage());
        BrowserGuestSession guest = await page.JoinAndAddDishAsync(
            fixture.InviteToken,
            name,
            dish.Name,
            quantity,
            submit);

        Recall<CleanupScope>().TrackDelete(
            $"group guest:{name}",
            () => new GroupReservationsApi(Actor.UsingTheApi())
                .DeleteGuestSessionAsync(fixture.GroupReservationId, guest.Id, fixture.OwnerToken));
    }

    private async Task<string> CreateReservationWithoutGroupAsync()
    {
        var fixture = Recall<GroupReservationFixture>();
        var tables = new TablesApi(Actor.UsingTheApi());
        var reservations = new ReservationsApi(Actor.UsingTheApi());
        var candidateTimes = Enumerable.Range(2, 14)
            .SelectMany(day => ReservationCandidateHours
                .Select(hour => DateTime.UtcNow.Date.AddDays(day).AddHours(hour)))
            .OrderBy(_ => Random.Shared.Next());

        foreach (DateTime startTime in candidateTimes)
        {
            await CaptureAndRequireSuccessAsync(
                await tables.GetAvailableTablesAsync("", 1, startTime),
                "Finding a free time for a reservation without a group");
            var table = response.Json<List<TableResponse>>()
                .SingleOrDefault(candidate => candidate.Id == fixture.Reservation.TableId);
            if (table is null)
                continue;

            await CaptureAndRequireSuccessAsync(
                await reservations.CreateReservationAsync(
                    SampleDataFactory.CreateReservationRequest(table.Id, startTime, guestCount: 1),
                    fixture.OwnerToken),
                "Creating a reservation without a group");
            var reservation = response.Json<ReservationResponse>();
            Recall<CleanupScope>().TrackDelete(
                $"group-less reservation:{reservation.Id}",
                () => reservations.CancelReservationAsync(reservation.Id, fixture.OwnerToken));
            return reservation.Id;
        }

        throw new InvalidOperationException(
            $"TESTTABLE_ID '{Settings.Current.RequireTestTable().TableId}' has no available slot for a single-guest reservation.");
    }

    private async Task CaptureAndRequireSuccessAsync(
        Microsoft.Playwright.IAPIResponse apiResponse,
        string action)
    {
        await response.CaptureAsync(apiResponse);
        if (!response.Succeeded)
            throw new InvalidOperationException($"{action} failed with {response.Status}: {response.Body}");
    }
}
