using Restaurant_Aqa_Framework.Api;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

public sealed class ViewGroupParticipants(string? bearerToken, string? groupReservationId = null) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var group = actor.Recall<GroupReservationFixture>();
        await actor.TheLastResponse().CaptureAsync(await new GroupReservationsApi(actor.UsingTheApi())
            .GetParticipantsAsync(groupReservationId ?? group.GroupReservationId, bearerToken));
    }
}
