using Restaurant_Aqa_Framework.Api.Dtos.GroupReservations;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

public sealed class TheGroupParticipants : IQuestion<GroupParticipantsResponse>
{
    public Task<GroupParticipantsResponse> AnsweredByAsync(Actor actor) =>
        Task.FromResult(actor.TheLastResponse().Json<GroupParticipantsResponse>());
}
