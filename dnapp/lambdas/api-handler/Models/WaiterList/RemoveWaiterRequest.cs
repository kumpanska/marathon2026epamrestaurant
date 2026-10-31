using System.Text.Json.Serialization;

namespace APIHandler.Models.WaiterList;

public class RemoveWaiterRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; set; }
}
