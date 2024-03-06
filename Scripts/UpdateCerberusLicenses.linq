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

// Paths to CSV files
public static string inputFile = @"C:\Temp\CsvOutput\SampleInput.csv";
public static string outputFile = @"C:\Temp\CsvOutput\SampleOutput.csv";

// Cerberus API info
public static string _cerberusUrl = "https://cerberus-core.azurewebsites.net";
public static string _productApiBase = "https://app-daltonsightapi-prod-scu.azurewebsites.net";
public static bool IsDryRun = true;

// API endpoints
public static Func<HttpClient, string, Task<User>> GetUserByEmailAddress = GetRequestAsync<User>(_cerberusUrl, email => $"user/{email}");
public static Func<HttpClient, string, Task<List<User>>> GetOrganizationUserAssignments = GetRequestAsync<List<User>>(_cerberusUrl, organizationId => $"organization/{organizationId}/user");
public static Func<HttpClient, Task<List<Organization>>> GetOrganizations = GetRequestAsync<List<Organization>>(_cerberusUrl, "organization");

//public List<CsvData> resultsFromCsv = new();

async Task Main()
{
	var client = new HttpClient();
	
	List<CsvData> usersFromCsv = ReadCSV();
	List<CsvData> nonCerberusUsersFromCsv = new();
	List<User> currentCerberusUsers = new();

	var count = 0;

	foreach (var user in usersFromCsv)
	{
		var email = user.Email;
		
		if (!string.IsNullOrEmpty(email))
		{
			// stop execution; for test only
			//if(count > 100)
			//	break;
			//count++;
			
			// Just have this here to check for emails that will break the API
			Regex regex = new Regex(@"^([\w-\.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([\w-]+\.)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$");
			Match match = regex.Match(email);
			if(match.Success)
			{
				// Get the user's licensing info
				var userLicenseInfo = await GetUserByEmailAddress(client, user.Email);
				
				if (userLicenseInfo.Id == null)
				{
					// From CSV but not in Cerberus
					nonCerberusUsersFromCsv.Add(user);
				}
				else
				{
					// In CSV and also in Cerberus
					currentCerberusUsers.Add(userLicenseInfo);
				}
			}
			else
			{
				Console.WriteLine($"User's email address {0} is not valid", email);
			}
		}
	}

	var cerberusUsersNotInOrg = await CompareUserListToCurrentOrgs(client, currentCerberusUsers);

	// iterate over all the orgs

	// for each org, get list of users

	// compare to the list of users to see which orgs users belong to

	// determine which users are not part of an org

	// determine which users are not part of the org that has all access, if any

	// add users not assigned to any org to the all access org

	// do we add other users to that org too?
	
	List<ConvertedUser> allUserStatuses = new();
	
	var cerberusUsersInOrg = currentCerberusUsers.Except(cerberusUsersNotInOrg).ToList();
	
	foreach(var singleUser in usersFromCsv)
	{
		var convertedUser = new ConvertedUser()
		{
			Email = singleUser.Email,
			Name = singleUser.Name,
			Title = singleUser.Title,
			HomeRadar = singleUser.Site,
			Org = singleUser.EntityOrOrganization // Org needs to be gotten from Cerberus where not present in file
		};
		
		if (cerberusUsersInOrg.Any(x => x.Email == singleUser.Email))
		{
			convertedUser.LicensingStatus = UserStatus.CerberusInOrg;
		}

		if (cerberusUsersNotInOrg.Any(x => x.Email == singleUser.Email))
		{
			convertedUser.LicensingStatus = UserStatus.CerberusNotInOrg;
		}
		
		// Default status is NotInCerberus so no need to re-assign
		
		allUserStatuses.Add(convertedUser);
	}
	
	CreateReportCSV(allUserStatuses);
}

/// <summary>
/// Helper method for making HttpGet requests to the Cerberus API
/// </summary>
public static Func<HttpClient, Task<TResponse>> GetRequestAsync<TResponse>(string baseUrl, string route) => async client =>
{
	var uri = new Uri(new Uri(baseUrl), route).Dump("Uri");
	var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{route}");

	request.Headers.Add("x-user-id", "RadiusLicensingLinq"); // does the specific name matter?

	var response = await client.SendAsync(request);

	if (response.IsSuccessStatusCode)
		return await response.Content.ReadFromJsonAsync<TResponse>();
		
	if(response.StatusCode == HttpStatusCode.NotFound)
		return await response.Content.ReadFromJsonAsync<TResponse>();

	throw new Exception($"{response.StatusCode}: {response.ReasonPhrase}. Route: {route}");
};

/// <summary>
/// Helper method that allows for string parameters
/// </summary>54 3
public static Func<HttpClient, string, Task<TResponse>> GetRequestAsync<TResponse>(string baseUrl, Func<string, string> routeFunc) =>
	(client, routeValue) => GetRequestAsync<TResponse>(baseUrl, routeFunc(routeValue))(client);

/// <summary>
/// Goes over the list of organizations and users to determine where each user belongs
/// </summary>
public async static Task<List<User>> CompareUserListToCurrentOrgs(HttpClient client, List<User> users)
{
	// contains mapping for each user's org(s)
	//var catalog = new Dictionary<string, List<Organization>>();
	
	// Retrieve the list of all current organizations
	var organizations = await GetOrganizations(client);
	
	List<User> usersInOrganization = new();
	
	foreach(var org in organizations)
	{
		var organizationUserList = await GetOrganizationUserAssignments(client, org.Id);

		if (organizationUserList.Any())
		{
			foreach (var user in users)
			{
				if (organizationUserList.Any(x => x.Email == user.Email))
				{
					usersInOrganization.Add(user);
				}

//				var matchedUser = organizationUserList.Where(x => x.Email == user.Email);
//
//				foreach (var match in matchedUser)
//				{
//					usersInOrganization.Add(match);
//				}
			}
		}
	}
	
	var usersNotInOrganization = users.Except(usersInOrganization);
	
	return usersNotInOrganization.ToList();
}

public async Task ReassignUsers()
{
	
}

/// <summary>
/// Read the data from the CSV file provided by radar science
/// </summary>
public static List<CsvData> ReadCSV()
{
	List<CsvData> resultsFromCsv = new();
	
	CsvConfiguration config = new CsvConfiguration(CultureInfo.InvariantCulture)
	{
		HasHeaderRecord = true
	};

	using var streamReader = new StreamReader(inputFile);
	using var csvReader = new CsvReader(streamReader, config);
	
	var cerberusUserData = csvReader.GetRecords<CsvData>();
	
	foreach(var userRecord in cerberusUserData)
	{
		resultsFromCsv.Add(userRecord);
	}
	
	return resultsFromCsv;
}

/// <summary>
/// Generate a sample CSV file with new user licenses
/// </summary>
public static void CreateReportCSV(List<ConvertedUser> usersWithStatuses)
{
	var csvConfig = new CsvConfiguration(CultureInfo.CurrentCulture)
	{
		HasHeaderRecord = true,
		Delimiter = ",",
		Encoding = Encoding.UTF8
	};

	using var writer = new StreamWriter(outputFile);
	using var csvWriter = new CsvWriter(writer, csvConfig);
	
	csvWriter.WriteRecords(usersWithStatuses);
}

public enum UserStatus {
	CerberusInOrg,
	CerberusNotInOrg,
	NotInCerberus
}

// Data models

public record User(string Id, string FirstName, string LastName, string Email, string MobileNumber);

public record Organization(string Id, string Name, string EmailDomain, string ExternalId, string ImageLinkId);

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

public class ConvertedUser
{
	public string Name { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string Email { get; set; } = string.Empty;
	public string HomeRadar { get; set; } = string.Empty;
	public string Org { get; set; } = string.Empty;
	public UserStatus LicensingStatus { get; set; } = UserStatus.NotInCerberus;
}