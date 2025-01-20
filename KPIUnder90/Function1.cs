using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace KPIUnder90
{
    public static class KPIUnder90
    {
        private static readonly string IoTHubConnectionString = Environment.GetEnvironmentVariable("IoTHubConnectionString");
        private static RegistryManager registryManager = RegistryManager.CreateFromConnectionString(IoTHubConnectionString);

        [Function("KPIUnder90")]
        public static async Task Run(
            [EventHubTrigger("kpialerts", Connection = "EventHubConnectionString")] string[] events,
            FunctionContext context)
        {
            var logger = context.GetLogger("KPIUnderLimitFunction");

            foreach (var eventData in events)
            {
                try
                {
                    logger.LogInformation($"Przetwarzanie zdarzenia: {eventData}");

                    // Parsowanie danych JSON
                    var eventDataJson = JsonDocument.Parse(eventData);
                    var workorderId = eventDataJson.RootElement.GetProperty("Workorderid").GetString();

                    if (string.IsNullOrEmpty(workorderId))
                    {
                        logger.LogWarning("Brakuje WorkorderId w danych zdarzenia.");
                        continue;
                    }

                    logger.LogInformation($"Wyodrêbniono WorkorderId: {workorderId}");

                    // Znalezienie DeviceId na podstawie WorkorderId
                    var deviceId = await GetDeviceIdByWorkorderId(workorderId, logger);
                    if (deviceId != null)
                    {
                        logger.LogInformation($"Znaleziono DeviceId: {deviceId} dla WorkorderId: {workorderId}");

                        // Aktualizacja Device Twin
                        await UpdateDeviceTwinAndInvokeEmergencyStop(deviceId, logger);
                    }
                    else
                    {
                        logger.LogWarning($"Nie znaleziono DeviceId dla WorkorderId: {workorderId}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"B³¹d podczas przetwarzania zdarzenia: {eventData}");
                }
            }

            logger.LogInformation("Wykonanie funkcji zakoñczone.");
        }

        private static async Task<string> GetDeviceIdByWorkorderId(string workorderId, ILogger logger)
        {
            try
            {
                // Tworzenie zapytania do IoT Hub
                var query = registryManager.CreateQuery($"SELECT * FROM devices WHERE properties.reported.workorderId = '{workorderId}'");

                if (!query.HasMoreResults)
                {
                    logger.LogWarning("Brak wyników dla WorkorderId: {WorkorderId}", workorderId);
                    return null;
                }

                // Pobieranie wyników jako Twin
                var twins = await query.GetNextAsTwinAsync();

                foreach (var twin in twins)
                {
                    // Sprawdzanie obecnoœci DeviceId
                    if (!string.IsNullOrEmpty(twin.DeviceId))
                    {
                        logger.LogInformation("Znaleziono DeviceId: {DeviceId} dla WorkorderId: {WorkorderId}", twin.DeviceId, workorderId);
                        return twin.DeviceId;
                    }
                }

                logger.LogWarning("Nie znaleziono DeviceId dla WorkorderId: {WorkorderId}", workorderId);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "B³¹d podczas zapytania Device Twin na podstawie WorkorderId");
                return null;
            }
        }


        private static async Task UpdateDeviceTwin(string deviceId, ILogger logger)
        {
            try
            {
                logger.LogInformation($"Aktualizacja Device Twin dla DeviceId: {deviceId}");

                var twin = await registryManager.GetTwinAsync(deviceId);

                // Bezpoœrednie pobranie wartoœci z domyœln¹ na 100
                int currentRate = twin.Properties.Desired.Contains("ProductionRate")
                    ? (int)twin.Properties.Desired["ProductionRate"]
                    : 100;

                var newRate = Math.Max(currentRate - 10, 0);
                twin.Properties.Desired["ProductionRate"] = newRate;

                await registryManager.UpdateTwinAsync(deviceId, twin, twin.ETag);
                logger.LogInformation($"Zaktualizowano Production Rate na {newRate}% dla DeviceId: {deviceId}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"B³¹d podczas aktualizacji Device Twin dla DeviceId: {deviceId}");
            }
        }

        private static async Task InvokeEmergencyStop(string deviceId, ILogger logger)
        {
            try
            {
                logger.LogInformation($"Wywo³ywanie EmergencyStop na DeviceId: {deviceId}");

                var serviceClient = ServiceClient.CreateFromConnectionString(IoTHubConnectionString);

                var methodInvocation = new CloudToDeviceMethod("EmergencyStop")
                {
                    ResponseTimeout = TimeSpan.FromSeconds(30)
                };

                var response = await serviceClient.InvokeDeviceMethodAsync(deviceId, methodInvocation);
                logger.LogInformation($"Wywo³ano EmergencyStop na DeviceId: {deviceId}. Status: {response.Status}");

                serviceClient.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"B³¹d podczas wywo³ywania EmergencyStop na DeviceId: {deviceId}");
            }
        }

        private static async Task UpdateDeviceTwinAndInvokeEmergencyStop(string deviceId, ILogger logger)
        {
            await UpdateDeviceTwin(deviceId, logger); // Zmniejszenie Production Rate
            await InvokeEmergencyStop(deviceId, logger); // Wywo³anie EmergencyStop
        }

    }
}
