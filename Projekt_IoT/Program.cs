using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Opc.UaFx.Client;
using System.Text;


public class ProductionLine
{
    public string Name { get; set; }
    public string OpcNode { get; set; }
    public string DeviceId { get; set; }
    public OpcClient OpcClient { get; set; }
    public string ConnectionString { get; set; }
    public DeviceClient IoTHubClient { get; set; }

    public ProductionLine(string name, string opcNode, string deviceId, string connectionString)
    {
        Name = name;
        OpcNode = opcNode;
        DeviceId = deviceId;
        ConnectionString = connectionString;
        OpcClient = new OpcClient("opc.tcp://localhost:4840/");
        try
        {
            OpcClient.Connect();
            Console.WriteLine($"Połączono z serwerem OPC UA: {opcNode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd połączenia z OPC UA: {ex.Message}");
        }
        IoTHubClient = DeviceClient.CreateFromConnectionString(connectionString);
    }

    public async Task SendTelemetryDataAsync()
    {
        try
        {
            var workorderNode = OpcClient.ReadNode($"{OpcNode}/WorkorderId");
            var productionStatusNode = OpcClient.ReadNode($"{OpcNode}/ProductionStatus");
            var goodCountNode = OpcClient.ReadNode($"{OpcNode}/GoodCount");
            var badCountNode = OpcClient.ReadNode($"{OpcNode}/BadCount");
            var temperatureNode = OpcClient.ReadNode($"{OpcNode}/Temperature");

            var workorderID = workorderNode.Value?.ToString();
            var productionStatus = productionStatusNode.Value as int?;
            var goodCount = goodCountNode.Value;
            var badCount = badCountNode.Value;
            var temperature = temperatureNode.Value as double?;

            if (workorderID == null || productionStatus == null || goodCount == null || badCount == null || temperature == null)
            {
                Console.WriteLine($"Błąd odczytu danych z urządzenia: {Name}");
                return;
            }

            var telemetryData = new
            {
                WorkorderID = workorderID,
                ProductionStatus = productionStatus,
                GoodCount = goodCount,
                BadCount = badCount,
                Temperature = temperature
            };

            string jsonPayload = JsonConvert.SerializeObject(telemetryData);
            var message = new Message(Encoding.UTF8.GetBytes(jsonPayload));

            await IoTHubClient.SendEventAsync(message);
            Console.WriteLine($"Wysłano dane dla {Name}: {jsonPayload}");

            await UpdateProductionRateAsync();
            await UpdateDeviceErrorAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas wysyłania danych z {Name}: {ex.Message}");
        }
    }

    private async Task UpdateProductionRateAsync()
    {
        Console.WriteLine("Aktualizaca Production Rate...");
        try
        {
            var twin = await IoTHubClient.GetTwinAsync();
            var desiredProductionRateLong = twin.Properties.Desired["ProductionRate"]?.Value;
            var desiredProductionRate = Convert.ToInt32(desiredProductionRateLong);

            if (desiredProductionRate == null)
            {
                Console.WriteLine("Oczekiwany Production Rate jest null lub niepoprawny.");
                return;
            }

            Console.WriteLine($"Oczekiwany Production Rate: {desiredProductionRate}");

            OpcClient.WriteNode($"{OpcNode}/ProductionRate", desiredProductionRate);
            Console.WriteLine($"Aktualizuję ProductionRate na: {desiredProductionRate}");

            var reportedPR = OpcClient.ReadNode($"{OpcNode}/ProductionRate").Value;
            var reportedProperties = new TwinCollection { ["ProductionRate"] = reportedPR };
            await IoTHubClient.UpdateReportedPropertiesAsync(reportedProperties);
            Console.WriteLine($"Raportowany PR to {reportedPR}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd {ex.Message}");
        }
    }

    private async Task UpdateDeviceErrorAsync()
    {
        Console.WriteLine("Aktualizacja błędów...");
        try
        {
            var workorderNode = OpcClient.ReadNode($"{OpcNode}/WorkorderId");
            var deviceErrorsNode = OpcClient.ReadNode($"{OpcNode}/DeviceError");
            var workorderID = workorderNode.Value?.ToString();
            var deviceErrors = deviceErrorsNode.Value as int?;

            if (!deviceErrors.HasValue)
            {
                Console.WriteLine($"Brak błędów urządzenia: {workorderID}");
                return;
            }

            var errorEvent = new
            {
                WorkorderID = workorderID,
                DeviceErrors = deviceErrors
            };
            var errorMessage = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(errorEvent)));
            await IoTHubClient.SendEventAsync(errorMessage);

            var reportedErr = new TwinCollection { ["DeviceError"] = deviceErrors };
            await IoTHubClient.UpdateReportedPropertiesAsync(reportedErr);
            Console.WriteLine($"Raportowane błędy to {deviceErrors}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd przy aktualizacji: {ex.Message}");
        }
    }

}

public class Program
{
    public static List<ProductionLine> ProductionLines = new List<ProductionLine>();

    public static async Task Main(string[] args)
    {
        AddProductionLine("Line_1", "ns=2;s=Device 1", "Device 1", "HostName=ZajeciaIoTHub.azure-devices.net;DeviceId=Device_1;SharedAccessKey=9RNu4sgwAsiS1A4kfDnT1MjZVRTOU0g3W7FSLl7QFhg=");
        AddProductionLine("Line_2", "ns=2;s=Device 2", "Device 2", "HostName=ZajeciaIoTHub.azure-devices.net;DeviceId=Device_2;SharedAccessKey=EbRbkgdy7rVAg4iGYYdLeBixBOQNXuqKs6n26e1UDsE=");
        AddProductionLine("Line_3", "ns=2;s=Device 3", "Device 3", "HostName=ZajeciaIoTHub.azure-devices.net;DeviceId=Device_3;SharedAccessKey=6r25pX0zNZ1cYs2H/h12hMNcGeiyga65lE+ukI7vCyI=");

        var tasks = new List<Task>();

        foreach (var line in ProductionLines)
        {
            tasks.Add(MonitorProductionLine(line));
        }

        await Task.WhenAll(tasks);
    }

    public static void AddProductionLine(string name, string opcNode, string deviceId, string connectionString)
    {
        var line = new ProductionLine(name, opcNode, deviceId, connectionString);
        ProductionLines.Add(line);
        Console.WriteLine($"Dodano linię: {name} z Connection String: {connectionString}");
    }

    public static async Task MonitorProductionLine(ProductionLine line)
    {
        while (true)
        {
            try
            {
                await line.SendTelemetryDataAsync();
                await Task.Delay(10000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd w linii {line.Name}: {ex.Message}");
            }
        }
    }
}