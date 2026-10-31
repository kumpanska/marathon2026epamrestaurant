using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Dtos.Dishes;
using Restaurant_Aqa_Framework.Api.Dtos.GroupReservations;
using Restaurant_Aqa_Framework.Api.Dtos.Reservations;
using Restaurant_Aqa_Framework.Api.Dtos.Tables;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Contexts;
using Restaurant_Aqa_Framework.Configuration;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

public sealed class SetUpGroupReservation(int guestCount = 8) : ITask
{
    private static readonly int[] CandidateHours = [11, 13, 15, 17, 19];

    public async Task PerformAsAsync(Actor actor)
    {
        var api = actor.UsingTheApi();
        var response = actor.TheLastResponse();
        var authApi = new AuthApi(api);
        var (tableId, waiterUsername, waiterPassword) = Settings.Current.RequireTestTable();

        await response.CaptureAsync(await authApi.SignInAsync(new SignInRequest(waiterUsername, waiterPassword)));
        EnsureSucceeded(response, "Signing in as the waiter assigned to the test table");
        var waiterToken = response.Json<SignInResponse>().IdToken;

        var tablesApi = new TablesApi(api);
        var (startTime, table) = await FindAvailableStartTimeAsync(tablesApi, response, tableId);

        var dishesApi = new DishesApi(api);
        await response.CaptureAsync(await dishesApi.CreateDishAsync(
            SampleDataFactory.CreateDishRequest([table.LocationId])));
        EnsureSucceeded(response, "Creating a sample dish for the test table");
        var dish = response.Json<DishResponse>();
        actor.Remember(dish);
        actor.Recall<CleanupScope>().TrackDelete(
            $"group pre-order dish:{dish.Id}",
            () => dishesApi.DeleteDishAsync(dish.Id, force: true, bearerToken: waiterToken));

        var visitor = SampleDataFactory.RegisterRequest();

        await response.CaptureAsync(await authApi.RegisterAsync(visitor));
        EnsureSucceeded(response, "Registering the group owner");

        await response.CaptureAsync(await authApi.SignInAsync(new SignInRequest(visitor.Email, visitor.Password)));
        EnsureSucceeded(response, "Signing in as the group owner");
        var ownerToken = response.Json<SignInResponse>().IdToken;

        var reservationsApi = new ReservationsApi(api);
        var reservationRequest = SampleDataFactory.CreateReservationRequest(
            table.Id,
            startTime,
            Math.Min(guestCount, table.Capacity));

        await response.CaptureAsync(await reservationsApi.CreateReservationAsync(reservationRequest, ownerToken));
        EnsureSucceeded(response, "Creating the group reservation");
        var reservation = response.Json<ReservationResponse>();

        actor.Recall<CleanupScope>().TrackDelete(
            $"group reservation:{reservation.Id}",
            () => reservationsApi.CancelReservationAsync(reservation.Id, ownerToken));

        var groupsApi = new GroupReservationsApi(api);
        await response.CaptureAsync(await groupsApi.GetInviteTokenAsync(reservation.Id, ownerToken));
        EnsureSucceeded(response, "Getting the group invite");
        var invite = response.Json<GroupInviteTokenResponse>();

        await response.CaptureAsync(await groupsApi.GetGroupFromInviteAsync(invite.Token));
        EnsureSucceeded(response, "Resolving the group invite");
        var group = response.Json<GroupInviteResponse>();

        actor.Remember(new GroupReservationFixture(
            reservation,
            group.GroupReservationId,
            ownerToken));
    }

    private static void EnsureSucceeded(ApiResponseContext response, string action)
    {
        if (!response.Succeeded)
            throw new InvalidOperationException($"{action} failed with {response.Status}: {response.Body}");
    }

    private static async Task<(DateTime StartTime, TableResponse Table)> FindAvailableStartTimeAsync(
        TablesApi tablesApi, ApiResponseContext response, string tableId)
    {
        var firstDate = DateTime.UtcNow.Date;
        var candidateTimes = Enumerable.Range(2, 14)
            .SelectMany(day => CandidateHours.Select(hour => firstDate.AddDays(day).AddHours(hour)))
            .OrderBy(_ => Random.Shared.Next());

        foreach (var startTime in candidateTimes)
        {
            await response.CaptureAsync(await tablesApi.GetAvailableTablesAsync("", 4, startTime));
            EnsureSucceeded(response, "Finding an available time for the configured test table");

            var table = response.Json<List<TableResponse>>()
                .SingleOrDefault(candidate => candidate.Id == tableId);
            if (table is not null)
                return (startTime, table);
        }

        throw new InvalidOperationException(
            $"TESTTABLE_ID '{tableId}' has no available 90-minute slot for at least 4 guests in the next 15 days.");
    }
}
