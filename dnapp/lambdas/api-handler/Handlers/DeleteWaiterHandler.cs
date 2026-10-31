using Amazon.CognitoIdentityProvider.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.WaiterList;
using APIHandler.Services.Waiters;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers;

public class DeleteWaiterHandler : IApiRequestHandler
{
    private readonly IWaiterService _waiterService;

    public DeleteWaiterHandler(IWaiterService waiterService)
    {
        _waiterService = waiterService ?? throw new ArgumentNullException(nameof(waiterService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!TryDeserializeRequest(request, out var removeWaiterRequest))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Invalid request body", context.Logger);
        }

        try
        {
            PersonalDataValidator.ValidateEmail(removeWaiterRequest!.Email);
        }
        catch (ValidationException ex)
        {
            return ResponseBuilder.CreateErrorResponse(400, ex.Message, context.Logger);
        }

        var email = removeWaiterRequest.Email.Trim().ToLower();

        return await HandleDeleteRequest(email, context.Logger);
    }

    private async Task<APIGatewayProxyResponse> HandleDeleteRequest(string email, ILambdaLogger logger)
    {
        if (!await _waiterService.IsWaiterInListAsync(email))
        {
            return ResponseBuilder.CreateErrorResponse(404, "Email not found", logger);
        }

        try
        {
            await _waiterService.RemoveWaiterFromListAsync(email);
        }
        catch (UserNotFoundException)
        {
            return ResponseBuilder.CreateErrorResponse(404, $"User with email {email} is not registered", logger);
        }

        return ResponseBuilder.CreateSuccessResponse("Email deleted successfully");
    }

    private static bool TryDeserializeRequest(APIGatewayProxyRequest request, out RemoveWaiterRequest? removeWaiterRequest)
    {
        try
        {
            removeWaiterRequest = JsonSerializer.Deserialize<RemoveWaiterRequest>(request.Body);
            return removeWaiterRequest != null;
        }
        catch (JsonException)
        {
            removeWaiterRequest = null;
            return false;
        }
    }
}