using Restaurant_Aqa_Framework.Api;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

public sealed class SubmitGuestCart(string guestName) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var guest = actor.Recall<GroupGuestSession>(guestName);
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupReservationsApi(actor.UsingTheApi())
            .SubmitGuestCartAsync(guest.Id, guest.SessionToken));

        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"Submitting '{guestName}'s cart failed with {response.Status}: {response.Body}");
    }
}
