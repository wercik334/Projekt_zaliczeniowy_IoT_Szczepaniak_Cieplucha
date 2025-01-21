# **System Zarządzania Produkcją IoT**

## **Opis projektu**

Projekt realizuje zarządzanie i monitorowanie linii produkcyjnych za pomocą technologii IoT. Składa się z:
1. Modułu głównego (`Program.cs`), który integruje urządzenia z serwerem OPC UA i przesyła dane telemetryczne do chmury Microsoft Azure IoT Hub.
2. Kod Funkcji Azure (`KPIUnder90/Function1.cs`), która obsługuje redukcję wydajności linii produkcyjnej oraz zatrzymanie w przypadku spadku wydajności linii produkcyjnej.
3. Kod Funkcji Azure (`DeviceErrors3/Function1.cs`), która obsługuje błędy urządzeń oraz zatrzymanie w przypadku notorycznego ich występowania.

## **Struktura projektu**

### Pliki projektu
- **`Program.cs`**:
  - Inicjalizuje linie produkcyjne i łączy je z serwerem OPC UA oraz Azure IoT Hub.
  - Odpowiada za przesyłanie danych telemetrycznych, takich jak odczyty temperatury, liczba poprawnych/niepoprawnych odczytów, status pracy.
  - Obsługuje metody chmurowe, takie jak `EmergencyStop` i `ResetErrorStatus`.
- **`Function1.cs`** (moduł KPIUnder90):
  - Monitoruje dane telemetryczne przesyłane przez IoT Hub do Event Hub za pomocą zadania Azure Stream Analytics.
  - Redukuje wydajność produkcji o 10, gdy wskaźniki KPI spadają poniżej progu 90%.
  - Wywołuje procedurę `EmergencyStop` w powyższym przypadku.
  - Funkcja została zaimplementowana w chmurze portalu Azure.
- **`Function1.cs`** (moduł DeviceErrors3):
  - Monitoruje liczbę błędów zgłaszanych przez urządzenia.
  - Wywołuje `EmergencyStop` po trzech wykrytych błędach na linii produkcyjnej.
  - Funkcja została zaimplementowana w chmurze portalu Azure.

## **Wymagania**

### Oprogramowanie:
- IIoT Simulator (udostępniony przez Prowadzących)
- OPC UA Sample Client (udostępniony przez Prowadzących)
- .NET SDK
- Microsoft Azure IoT Hub
- Biblioteki NuGet:
  - Microsoft Azure Devices Client
  - Microsoft Azure Devices Shared
  - Newtonsoft Json
  - Opc UaFx Client
  - System Text

 ## **Kolejność uruchomienia**

1. W IIoT Simulatorze dodajemy wybraną ilość urządzeń. Tyle samo urządzeń dodajemy w IoT Hubie. Modyfikujemy kod `Program.cs` dodając tyle linii produkcyjnych korzystając z funkcji AddProductionLine.
2. Ustawiamy status produkcji urządzeń na 1 za pomocą przycisku "Start". Następnie uruchamiamy OPC UA Sample Client i konfigurujemy połączenie na opc.tcp://localhost:4840/. (dokumentacja do konfiguracji IIoT Sim oraz OPC Server udostępniona przez Prowadzących).
3. W Azure Portal uruchamiamy następujące zadania w Azure Stream Analytics:
  - iot_job1 - wysyła co minutę informacje do konteneru "konteneriotblob" w koncie magazynu "magazyniot", o średniej, maksymalnej oraz minimalnej temperaturze z ostatnich 5 minut dla każdego urządzenia.
  - iotjob2_KPI - wysyła co 5 minut informacje do konteneru "kpi" w koncie magazynu "tamtenniedziala" o wyliczonym KPI z ostatnich 5 minut.
  - kpi90 - wysyła co 5 te same informacje co iotjob2_KPI do Event Hub "kpialerts" filtrując wyniki zapytania tak by KPI < 0.9.
  - ErrorCount - wysyła co minutę informacje i do kontenera "errors3" oraz do Event Hub "errorcount". Do magazynu wysyłany jest json z informacjami, gdy wystąpią ponad 3 błędy na jednym urządzeniu w ciągu minuty. Do Event Hub wysyłana jest wiadomość przy każdej zmianie flagi błedu na danym urządzeniu.
4. Uruchamiamy Azure Functions:
    - KPIUnder90 - opis wyżej (Function1.cs moduł KPIUnder90)
    - DeviceErrors3 - opis wyżej (Function1.cs moduł DeviceErrors3)
5. Uruchomienie alertu w Event Hub "EventErrorIot".
6. Uruchomienie agenta `Program.cs`.

## **Autorzy**
- **Weronika Cieplucha**
- **Wojciech Szczepaniak**
