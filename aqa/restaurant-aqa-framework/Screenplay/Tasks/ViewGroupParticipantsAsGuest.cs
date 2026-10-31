using Restaurant_Aqa_Framework.Api;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

public sealed class ViewGroupParticipantsAsGuest(string guestName) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var group = actor.Recall<GroupReservationFixture>();
        var guest = actor.Recall<GroupGuestSession>(guestName);
        await actor.TheLastResponse().CaptureAsync(await new GroupReservationsApi(actor.UsingTheApi())
            .GetParticipantsAsync(group.GroupReservationId, guest.SessionToken));
    }
}
