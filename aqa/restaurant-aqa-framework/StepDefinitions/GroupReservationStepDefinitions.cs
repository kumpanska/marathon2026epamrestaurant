using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Screenplay.Tasks;

namespace Restaurant_Aqa_Framework.StepDefinitions;

[Binding]
public sealed class GroupReservationStepDefinitions(Actor actor) : StepDefinitionBase(actor)
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
}
