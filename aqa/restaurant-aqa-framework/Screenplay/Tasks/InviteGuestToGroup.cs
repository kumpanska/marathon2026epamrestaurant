using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupReservations;
using Restaurant_Aqa_Framework.Api.Seeding;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

public sealed class InviteGuestToGroup(string name) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var group = actor.Recall<GroupReservationFixture>();
        var api = new GroupReservationsApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await api.CreateGuestSessionAsync(
            new CreateGuestSessionRequest(group.GroupReservationId, name)));

        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"Inviting guest '{name}' failed with {response.Status}: {response.Body}");

        var created = response.Json<GuestSessionCreatedResponse>();
        var guest = new GroupGuestSession(created.SessionId, name, created.SessionToken);
        actor.Remember(name, guest);

        actor.Recall<CleanupScope>().TrackDelete(
            $"group guest:{name}",
            () => api.DeleteGuestSessionAsync(group.GroupReservationId, guest.Id, group.OwnerToken));
    }
}
