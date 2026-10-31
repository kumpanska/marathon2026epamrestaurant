using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Seeding;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

public sealed class ViewGroupParticipantsAsAnotherCustomer : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var api = actor.UsingTheApi();
        var response = actor.TheLastResponse();
        var authApi = new AuthApi(api);
        var visitor = SampleDataFactory.RegisterRequest();

        await response.CaptureAsync(await authApi.RegisterAsync(visitor));
        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"Registering a non-owner customer failed with {response.Status}: {response.Body}");

        await response.CaptureAsync(await authApi.SignInAsync(new SignInRequest(visitor.Email, visitor.Password)));
        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"Signing in as a non-owner customer failed with {response.Status}: {response.Body}");

        var token = response.Json<SignInResponse>().IdToken;
        var group = actor.Recall<GroupReservationFixture>();
        await response.CaptureAsync(await new GroupReservationsApi(api)
            .GetParticipantsAsync(group.GroupReservationId, token));
    }
}
