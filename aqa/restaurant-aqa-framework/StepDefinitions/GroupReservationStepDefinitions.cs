using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Dtos.Dishes;
using Restaurant_Aqa_Framework.Api.Dtos.GroupReservations;
using Restaurant_Aqa_Framework.Api.Dtos.Orders;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Contexts;
using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Screenplay.Tasks;

namespace Restaurant_Aqa_Framework.StepDefinitions;

[Binding]
public sealed class GroupReservationStepDefinitions(Actor actor, ApiResponseContext response) : StepDefinitionBase(actor)
{
    [Given("the manager has set up a table for {int} guests")]
    public Task GivenManagerHasSetUpATable(int guestCount) =>
        Actor.AttemptsToAsync(new AddTable(guestCount));

    [Given("a customer owns a group reservation")]
    public Task GivenCustomerOwnsAGroupReservation() =>
        Actor.AttemptsToAsync(new SetUpGroupReservation());

    [Given("guest {string} has joined the group")]
    public Task GivenGuestHasJoined(string guestName) =>
        Actor.AttemptsToAsync(new InviteGuestToGroup(guestName));

    [Given("guest {string} adds {int} portions of the dish")]
    [When("guest {string} adds {int} portions of the dish")]
    public Task GivenGuestAddsDish(string guestName, int quantity) =>
        Actor.AttemptsToAsync(new AddDishToGuestCart(guestName, quantity));

    [Given("guest {string} submits their basket")]
    [When("guest {string} submits their basket")]
    public Task GivenGuestSubmitsBasket(string guestName) =>
        Actor.AttemptsToAsync(new SubmitGuestCart(guestName));

    [When("the owner views the group participants")]
    public Task WhenOwnerViewsParticipants()
    {
        var group = Recall<GroupReservationFixture>();
        return Actor.AttemptsToAsync(new ViewGroupParticipants(group.OwnerToken));
    }

    [When("a waiter who does not own the reservation views the group participants")]
    public async Task WhenWaiterViewsParticipants() =>
        await Actor.AttemptsToAsync(new ViewGroupParticipants(
            (await Actor.AsksForAsync(new TheWaiterSession())).IdToken));

    [When("an unauthenticated visitor views the group participants")]
    public Task WhenUnauthenticatedVisitorViewsParticipants() =>
        Actor.AttemptsToAsync(new ViewGroupParticipants(null));

    [When("another customer views the group participants")]
    public Task WhenAnotherCustomerViewsParticipants() =>
        Actor.AttemptsToAsync(new ViewGroupParticipantsAsAnotherCustomer());

    [When("guest {string} views the group participants")]
    public Task WhenGuestViewsParticipants(string guestName) =>
        Actor.AttemptsToAsync(new ViewGroupParticipantsAsGuest(guestName));

    [When("the owner views a group that does not exist")]
    public Task WhenOwnerViewsMissingGroup()
    {
        var group = Recall<GroupReservationFixture>();
        return Actor.AttemptsToAsync(new ViewGroupParticipants(group.OwnerToken, Guid.NewGuid().ToString()));
    }

    [Given("an order exists for the group reservation")]
    public async Task GivenOrderExistsForGroupReservation()
    {
        var group = Recall<GroupReservationFixture>();
        var ordersApi = new OrdersApi(Actor.UsingTheApi());
        await response.CaptureAsync(await ordersApi.CreateOrderAsync(
            new CreateOrderRequest(group.Reservation.Id, []),
            group.OwnerToken));

        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"Creating the group pre-order failed with {response.Status}: {response.Body}");

        Recall<CleanupScope>().TrackDelete(
            $"group pre-order:{group.Reservation.Id}",
            () => ordersApi.DeleteOrderAsync(group.Reservation.Id, group.OwnerToken));
    }

    [Given("the owner adds {int} portion of the dish to their basket")]
    public async Task GivenOwnerAddsDish(int quantity)
    {
        var group = Recall<GroupReservationFixture>();
        var groupsApi = new GroupReservationsApi(Actor.UsingTheApi());
        await response.CaptureAsync(await groupsApi.GetOwnerGuestSessionAsync(
            group.Reservation.Id,
            group.OwnerToken));
        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"Getting the owner's group session failed with {response.Status}: {response.Body}");

        var ownerSession = response.Json<OwnerGuestSessionResponse>();
        var dish = Recall<DishResponse>();
        await response.CaptureAsync(await groupsApi.AddDishAsync(
            ownerSession.GuestSessionId,
            dish.Id,
            quantity,
            group.OwnerToken));
        EnsureSucceeded("Adding a dish to the owner's basket");
        Actor.Remember("owner-group-session", ownerSession);
    }

    [Given("the owner submits their basket")]
    public async Task GivenOwnerSubmitsBasket()
    {
        var group = Recall<GroupReservationFixture>();
        var ownerSession = Recall<OwnerGuestSessionResponse>("owner-group-session");
        await response.CaptureAsync(await new GroupReservationsApi(Actor.UsingTheApi())
            .SubmitGuestCartAsync(ownerSession.GuestSessionId, group.OwnerToken));
        EnsureSucceeded("Submitting the owner's basket");
    }

    [Given("guest {string} submits their empty basket")]
    public async Task GivenGuestSubmitsEmptyBasket(string guestName)
    {
        var guest = Recall<GroupGuestSession>(guestName);
        await response.CaptureAsync(await new GroupReservationsApi(Actor.UsingTheApi())
            .SubmitGuestCartAsync(guest.Id, guest.SessionToken));
        EnsureSucceeded($"Submitting '{guestName}'s empty basket");
    }

    [When("the owner confirms the consolidated pre-order")]
    public Task WhenOwnerConfirmsPreOrder()
    {
        var group = Recall<GroupReservationFixture>();
        return ConfirmGroupAsync(group.GroupReservationId, group.Reservation.Id, group.OwnerToken);
    }

    [When("the owner confirms the consolidated pre-order again")]
    public Task WhenOwnerConfirmsPreOrderAgain() => WhenOwnerConfirmsPreOrder();

    [When("the owner confirms a reservation that does not exist")]
    public Task WhenOwnerConfirmsMissingReservation()
    {
        var group = Recall<GroupReservationFixture>();
        return ConfirmGroupAsync(group.GroupReservationId, Guid.NewGuid().ToString(), group.OwnerToken);
    }

    [When("the owner confirms a group that does not exist")]
    public Task WhenOwnerConfirmsMissingGroup()
    {
        var group = Recall<GroupReservationFixture>();
        return ConfirmGroupAsync(Guid.NewGuid().ToString(), group.Reservation.Id, group.OwnerToken);
    }

    [When("another customer confirms the consolidated pre-order")]
    public async Task WhenAnotherCustomerConfirmsPreOrder()
    {
        var api = Actor.UsingTheApi();
        var authApi = new AuthApi(api);
        var visitor = SampleDataFactory.RegisterRequest();
        await response.CaptureAsync(await authApi.RegisterAsync(visitor));
        EnsureSucceeded("Registering a non-owner customer");
        await response.CaptureAsync(await authApi.SignInAsync(
            new SignInRequest(visitor.Email, visitor.Password)));
        EnsureSucceeded("Signing in as a non-owner customer");
        var token = response.Json<SignInResponse>().IdToken;
        var group = Recall<GroupReservationFixture>();
        await ConfirmGroupAsync(group.GroupReservationId, group.Reservation.Id, token);
    }

    [Then("the owner sees that the pre-order was transferred")]
    public async Task ThenOwnerSeesPreOrderTransferred()
    {
        var dish = Recall<DishResponse>();
        await Assert.That(response.Status).IsEqualTo(200).Because($"response body was: {response.Body}");
        var consolidatedDishes = response.Json<List<ConsolidatedDishResponse>>();
        await Assert.That(consolidatedDishes).Count().IsEqualTo(1);
        await Assert.That(consolidatedDishes.Single().DishId).IsEqualTo(dish.Id);
        await Assert.That(consolidatedDishes.Single().Quantity).IsEqualTo(6);
    }

    [Then("the group booking is submitted")]
    public async Task ThenGroupBookingIsSubmitted()
    {
        var group = Recall<GroupReservationFixture>();
        await response.CaptureAsync(await new GroupReservationsApi(Actor.UsingTheApi())
            .GetGroupStatusAsync(group.GroupReservationId));
        EnsureSucceeded("Reading the group reservation status");
        var status = response.Json<GroupReservationStatusResponse>();
        await Assert.That(status.Status).IsEqualTo("Submitted");
    }

    [Then("the waiter sees a customer-submitted order with {int} portions of the dish")]
    public async Task ThenWaiterSeesCustomerSubmittedOrder(int quantity)
    {
        var group = Recall<GroupReservationFixture>();
        var waiter = await Actor.AsksForAsync(new TheWaiterSession());
        await response.CaptureAsync(await new OrdersApi(Actor.UsingTheApi())
            .GetOrderAsync(group.Reservation.Id, waiter.IdToken));
        EnsureSucceeded("Reading the submitted order as the waiter");
        var order = response.Json<GetOrderResponse>();
        var dish = Recall<DishResponse>();
        await Assert.That(order.Status).IsEqualTo(OrderStatus.SubmittedByCustomer);
        await Assert.That(order.OrderItems).Count().IsEqualTo(1);
        await Assert.That(order.OrderItems.Single().Dish.Id).IsEqualTo(dish.Id);
        await Assert.That(order.OrderItems.Single().Quantity).IsEqualTo(quantity);
    }

    [Then("confirmation is rejected with {string}")]
    public async Task ThenConfirmationIsRejected(string message)
    {
        await Assert.That(response.Succeeded).IsFalse().Because($"the request unexpectedly succeeded: {response.Body}");
        await Assert.That(response.ErrorMessage).IsEqualTo(message);
    }

    [Then("the owner sees {int} guest participants")]
    public async Task ThenOwnerSeesGuestParticipants(int expectedCount)
    {
        var guests = (await Actor.AsksForAsync(new TheGroupParticipants()))
            .Participants.Where(participant => participant.Type == "Guest").ToList();

        await Assert.That(guests).Count().IsEqualTo(expectedCount);
    }

    [Then("guest {string} is listed with {int} portions of the dish")]
    public async Task ThenGuestIsListedWithDishQuantity(string guestName, int quantity)
    {
        var guest = (await Actor.AsksForAsync(new TheGroupParticipants()))
            .Participants.Single(participant => participant.Name == guestName);

        await Assert.That(guest.Items).Count().IsEqualTo(1);
        await Assert.That(guest.Items.Single().Quantity).IsEqualTo(quantity);
        await Assert.That(guest.Items.Single().Dish.Name).IsNotEmpty();
    }

    [Then("guest {string} has the correct subtotal for {int} portions")]
    public async Task ThenGuestHasCorrectSubtotal(string guestName, int quantity)
    {
        var group = await Actor.AsksForAsync(new TheGroupParticipants());
        var guest = group.Participants.Single(participant => participant.Name == guestName);
        var dish = guest.Items.Single().Dish;

        await Assert.That(guest.Total).IsEqualTo(dish.Price * quantity);
    }

    [Then("guest {string} is listed with an unconfirmed empty basket")]
    public async Task ThenGuestHasUnconfirmedEmptyBasket(string guestName)
    {
        var guest = (await Actor.AsksForAsync(new TheGroupParticipants()))
            .Participants.Single(participant => participant.Name == guestName);

        await Assert.That(guest.Items).IsEmpty();
        await Assert.That(guest.Total).IsEqualTo(0m);
        await Assert.That(guest.IsSubmitted).IsFalse();
    }

    [Then("the group total equals the sum of participant totals")]
    public async Task ThenGroupTotalEqualsParticipantTotals()
    {
        var group = await Actor.AsksForAsync(new TheGroupParticipants());

        await Assert.That(group.Total).IsEqualTo(
            group.Participants.Where(participant => participant.Type == "Guest").Sum(participant => participant.Total));
    }

    [Then("guest {string} is marked as submitted")]
    public async Task ThenGuestIsMarkedSubmitted(string guestName)
    {
        var guest = (await Actor.AsksForAsync(new TheGroupParticipants()))
            .Participants.Single(participant => participant.Name == guestName);

        await Assert.That(guest.IsSubmitted).IsTrue();
    }

    [Then("guest {string} is not marked as submitted")]
    public async Task ThenGuestIsNotMarkedSubmitted(string guestName)
    {
        var guest = (await Actor.AsksForAsync(new TheGroupParticipants()))
            .Participants.Single(participant => participant.Name == guestName);

        await Assert.That(guest.IsSubmitted).IsFalse();
    }

    [Then("the group has no participants and a total of zero")]
    public async Task ThenGroupIsEmpty()
    {
        var group = await Actor.AsksForAsync(new TheGroupParticipants());

        await Assert.That(group.Participants).IsEmpty();
        await Assert.That(group.Total).IsEqualTo(0m);
    }

    [Then("the waiter is forbidden from viewing the group participants")]
    public async Task ThenWaiterIsForbidden()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(403).Because($"response body was: {outcome.Body}");
    }

    [Then("the unauthenticated visitor is refused")]
    public async Task ThenUnauthenticatedVisitorIsRefused()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(401).Because($"response body was: {outcome.Body}");
    }

    [Then("the non-owner is forbidden from viewing the group participants")]
    public async Task ThenNonOwnerIsForbidden()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(403).Because($"response body was: {outcome.Body}");
    }

    [Then("the guest is refused from viewing the group participants")]
    public async Task ThenGuestIsRefused()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(401).Because($"response body was: {outcome.Body}");
    }

    [Then("the missing group is not found")]
    public async Task ThenMissingGroupIsNotFound()
    {
        var group = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(group.Status).IsEqualTo(404).Because($"response body was: {group.Body}");
    }

    private async Task ConfirmGroupAsync(string groupReservationId, string reservationId, string token)
    {
        await response.CaptureAsync(await new GroupReservationsApi(Actor.UsingTheApi())
            .SubmitGroupReservationAsync(groupReservationId, reservationId, token));
    }

    private void EnsureSucceeded(string action)
    {
        if (!response.Succeeded)
            throw new InvalidOperationException($"{action} failed with {response.Status}: {response.Body}");
    }
}
