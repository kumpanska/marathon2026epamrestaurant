using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GroupReservations;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GroupReservations;

public class SubmitGroupReservationHandler(IGroupReservationService groupReservationService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out string? groupReservationId))
            return ResponseBuilder.CreateErrorResponse(400, "Group reservation ID is required.", context.Logger);

        string? userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);

        if (!TryDeserializeRequestBody(request.Body, out SubmitGroupReservationRequest? submitRequest) ||
            string.IsNullOrWhiteSpace(submitRequest?.ReservationId))
        {
            return ResponseBuilder.CreateErrorResponse(
                400,
                "Request body must contain a reservationId.",
                context.Logger);
        }

        try
        {
            var consolidatedDishes = await groupReservationService.SubmitGroupReservationAsync(
                userId,
                groupReservationId,
                submitRequest.ReservationId);

            return ResponseBuilder.CreateJsonResponse(200, consolidatedDishes);
        }
        catch (GroupReservationNotFoundException exception)
        {
            return ResponseBuilder.CreateErrorResponse(404, exception.Message, context.Logger);
        }
        catch (ReservationNotFoundException exception)
        {
            return ResponseBuilder.CreateErrorResponse(404, exception.Message, context.Logger);
        }
        catch (ActionForbiddenException exception)
        {
            return ResponseBuilder.CreateErrorResponse(403, exception.Message, context.Logger);
        }
        catch (ValidationException exception)
        {
            return ResponseBuilder.CreateErrorResponse(400, exception.Message, context.Logger);
        }
        catch (Exception exception)
        {
            return ResponseBuilder.CreateErrorResponse(500, "Internal server error.", context.Logger, exception);
        }
    }

    private static bool TryDeserializeRequestBody(
        string? body,
        out SubmitGroupReservationRequest? submitRequest)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            submitRequest = null;
            return false;
        }

        try
        {
            submitRequest = JsonSerializer.Deserialize<SubmitGroupReservationRequest>(body);
            return submitRequest is not null;
        }
        catch (JsonException)
        {
            submitRequest = null;
            return false;
        }
    }
}
