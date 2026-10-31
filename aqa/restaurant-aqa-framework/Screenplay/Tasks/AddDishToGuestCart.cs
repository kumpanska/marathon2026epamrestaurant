using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Dishes;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

public sealed class AddDishToGuestCart(string guestName, int quantity) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var guest = actor.Recall<GroupGuestSession>(guestName);
        var dish = actor.Recall<DishResponse>();
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupReservationsApi(actor.UsingTheApi())
            .AddDishAsync(guest.Id, dish.Id, quantity, guest.SessionToken));

        if (!response.Succeeded)
            throw new InvalidOperationException(
                $"Adding the dish to '{guestName}'s cart failed with {response.Status}: {response.Body}");
    }
}
