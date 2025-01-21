using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DeviceErrors3
{
    public static class DeviceErrors3
    {
        private static readonly string IoTHubConnectionString = Environment.GetEnvironmentVariable("IoTHubConnectionString");
        private static RegistryManager registryManager = RegistryManager.CreateFromConnectionString(IoTHubConnectionString);

        private static ConcurrentDictionary<string, int> MessageCounts = new();

        [Function("DeviceErrors3")]
        public static async Task Run(
            [EventHubTrigger("errorcount", Connection = "EventHubConnectionString")] string[] events,
            FunctionContext context)
        {
            var logger = context.GetLogger("DeviceErrors3");

            foreach (var eventData in events)
            {
                try
                {
                    logger.LogInformation($"Odbiera wydarzenie: {eventData}");

                    var eventDataJson = JsonDocument.Parse(eventData);
                    var workorderId = eventDataJson.RootElement.GetProperty("Workorderid").GetString();

                    if (string.IsNullOrEmpty(workorderId))
                    {
                        logger.LogWarning("Nie ma takiego WorkorderId w Evencie.");
                        continue;
                    }

                    logger.LogInformation($"Wyci¹gniête WorkorderId: {workorderId}");

                    MessageCounts.AddOrUpdate(workorderId, 1, (_, currentCount) => currentCount + 1);

                    if (MessageCounts[workorderId] > 3)
                    {
                        logger.LogInformation($"Osi¹gniêto powy¿ej 3 b³êdów dla WorkorderId: {workorderId}");

                        var deviceId = await GetDeviceIdByWorkorderId(workorderId, logger);
                        if (deviceId != null)
                        {
                            logger.LogInformation($"Znaleziono DeviceId: {deviceId} dla WorkorderId: {workorderId}");

                            await InvokeEmergencyStop(deviceId, logger);

                            MessageCounts[workorderId] = 0;
                        }
                        else
                        {
                            logger.LogWarning($"Brak DeviceId dla WorkorderId: {workorderId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"B³¹d w przetwarzaniu Event: {eventData}");
                }
            }

            logger.LogInformation("Funkcja wystosowana.");
        }

        private static async Task<string> GetDeviceIdByWorkorderId(string workorderId, ILogger logger)
        {
            try
            {
                var query = registryManager.CreateQuery($"SELECT * FROM devices WHERE properties.reported.workorderId = '{workorderId}'");

                if (!query.HasMoreResults)
                {
                    logger.LogWarning("Brak wyników dla WorkorderId: {WorkorderId}", workorderId);
                    return null;
                }

                var twins = await query.GetNextAsTwinAsync();
                foreach (var twin in twins)
                {
                    if (!string.IsNullOrEmpty(twin.DeviceId))
                    {
                        logger.LogInformation("Znaleziono DeviceId: {DeviceId} dla WorkorderId: {WorkorderId}", twin.DeviceId, workorderId);
                        return twin.DeviceId;
                    }
                }

                logger.LogWarning("Brak DeviceId dla WorkorderId: {WorkorderId}", workorderId);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "B³¹d w wyszukiwaniu Device Twin dla WorkorderId");
                return null;
            }
        }

        private static async Task InvokeEmergencyStop(string deviceId, ILogger logger)
        {
            try
            {
                logger.LogInformation($"Zastosowanie EmergencyStop na DeviceId: {deviceId}");

                var serviceClient = ServiceClient.CreateFromConnectionString(IoTHubConnectionString);

                var methodInvocation = new CloudToDeviceMethod("EmergencyStop")
                {
                    ResponseTimeout = TimeSpan.FromSeconds(30)
                };

                var response = await serviceClient.InvokeDeviceMethodAsync(deviceId, methodInvocation);
                logger.LogInformation($"EmergencyStop wywo³ane na DeviceId: {deviceId}. Status: {response.Status}");

                serviceClient.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"B³¹d wywo³ania EmergencyStop na DeviceId: {deviceId}");
            }
        }
    }
}
