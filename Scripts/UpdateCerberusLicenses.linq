<Query Kind="Program">
  <NuGetReference>CsvHelper</NuGetReference>
  <NuGetReference>Microsoft.Extensions.Http</NuGetReference>
  <NuGetReference>System.Net.Http</NuGetReference>
  <NuGetReference>System.Net.Http.Json</NuGetReference>
  <NuGetReference>System.Security.Cryptography.X509Certificates</NuGetReference>
  <Namespace>CsvHelper</Namespace>
  <Namespace>CsvHelper.Configuration</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>System.Net.Http.Json</Namespace>
</Query>

public static string inputFile = @"C:\Temp\CsvOutput\SampleInput.csv";
public static string outputFile = @"C:\Temp\CsvOutput\SampleOutput.csv";

public static string _cerberusUrl = "https://cerberus-core.azurewebsites.net";
public static string _productApiBase = "https://app-daltonsightapi-prod-scu.azurewebsites.net";
public static bool IsDryRun = true;

public static Func<HttpClient, string, Task<User>> GetUserByEmailAddress = GetRequestAsync<User>(_cerberusUrl, email => $"user/{email}");

//public List<CsvData> resultsFromCsv = new();

void Main()
{
	var listOfCerberusUsers = ReadCSV();

	foreach (var cerberusUser in listOfCerberusUsers)
	{
		// TODO
	}
}

public static Func<HttpClient, Task<TResponse>> GetRequestAsync<TResponse>(string baseUrl, string route) => async client =>
{
	var uri = new Uri(new Uri(baseUrl), route).Dump("Uri");
	var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{route}");

	request.Headers.Add("x-user-id", "RadiusLicensingLinq");

	var response = await client.SendAsync(request);

	if (response.IsSuccessStatusCode)
		return await response.Content.ReadFromJsonAsync<TResponse>();

	throw new Exception($"{response.StatusCode}: {response.ReasonPhrase}. Route: {route}");
};

public static Func<HttpClient, string, Task<TResponse>> GetRequestAsync<TResponse>(string baseUrl, Func<string, string> routeFunc) =>
	(client, routeValue) => GetRequestAsync<TResponse>(baseUrl, routeFunc(routeValue))(client);

public static List<CsvData> ReadCSV()
{
	List<CsvData> resultsFromCsv = new();
	
	CsvConfiguration config = new CsvConfiguration(CultureInfo.InvariantCulture)
	{
		HasHeaderRecord = true
	};

	using var streamReader = new StreamReader(inputFile);
	using var csvReader = new CsvReader(streamReader, config); // CultureInfo.InvariantCulture);
	
	var cerberusUserData = csvReader.GetRecords<CsvData>();
	
	foreach(var pieceOfData in cerberusUserData)
	{
		resultsFromCsv.Add(pieceOfData);
	}
	
	return resultsFromCsv;
}

public static void GenerateCSV()
{
	
}


public record User(string Id, string FirstName, string LastName, string Email, string MobileNumber);

public class CsvData
{
	public string Name { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string Email { get; set; } = string.Empty;
	public string EntityOrOrganization { get; set; } = string.Empty;
	public string SalesforceId { get; set; } = string.Empty;
	public string Phone { get; set; } = string.Empty;
	public string Site { get; set; } = string.Empty;
	public string HasGRAccess { get; set; } = string.Empty; // should be a bool instead
	public string UserCategory { get; set; } = string.Empty;
	public string AccessExpiration { get; set; } = string.Empty;
}