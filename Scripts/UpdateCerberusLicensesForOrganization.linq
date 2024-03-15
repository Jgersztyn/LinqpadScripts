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

// Cerberus API info
public static string _cerberusUrl = "https://cerberus-core.azurewebsites.net";
//public static string _productApiBase = "https://app-daltonsightapi-prod-scu.azurewebsites.net";
public static bool IsDryRun = true;

// GET API endpoints
public static Func<HttpClient, string, Task<User>> GetUserByEmailAddress = GetRequestAsync<User>(_cerberusUrl, email => $"user/{email}");
public static Func<HttpClient, string, Task<List<User>>> GetOrganizationUserAssignments = GetRequestAsync<List<User>>(_cerberusUrl, organizationId => $"organization/{organizationId}/user");
public static Func<HttpClient, Task<List<Organization>>> GetOrganizations = GetRequestAsync<List<Organization>>(_cerberusUrl, "organization");
public static Func<HttpClient, string, Task<List<LicenseAssignment>>> GetLicenseAssignmentsForAnOrganization = GetRequestAsync<List<LicenseAssignment>>(_cerberusUrl, organizationId => $"organization/{organizationId}/license-assignment");
public static Func<HttpClient, string, Task<List<LicenseAssignmentRequest>>> GetLicenseRequestsForAnOrganization = GetRequestAsync<List<LicenseAssignmentRequest>>(_cerberusUrl, organizationId => $"organization/{organizationId}/license");

// POST API endpoints
public static async Task<AddOrganizationLicenseAssignmentCommand> AddOrganizationLicenseAssignment(string organizationId, string licenseId, int requestedLength, HttpClient client) =>
	await SendRequestAsync(new AddOrganizationLicenseAssignmentCommand(organizationId, licenseId, requestedLength), _cerberusUrl, $"organization/{organizationId}/license/{licenseId},", client);

public static async Task<AddOrganizationLicenseApprovalCommand> AddOrganizationLicenseApproval(string organizationId, string licenseAssignmentRequestId, DateTime expiration, HttpClient client) =>
	await SendRequestAsync(new AddOrganizationLicenseApprovalCommand(organizationId, licenseAssignmentRequestId, expiration), _cerberusUrl, $"organization/{organizationId}/license/{licenseAssignmentRequestId},", client);

//public async Task<LicenseAssignmentDeactivation> DeactivateLicenseAssignment(DeactivateLicenseRequest request, string organizationId, string licenseAssignmentRequestId, CancellationToken cancellationToken) =>
//	await SendRequestAsync(licenseAssignmentRequestId, organizationId, request.DeactivationReason, request.Reason, cancellationToken);

// test endpoint; maybe not needed
public static Func<HttpClient, string, Task<LicenseAutoApproveDetails>> GetLicenseAutoApproveDetails = GetRequestAsync<LicenseAutoApproveDetails>(_cerberusUrl, licenseId => $"license/{licenseId}/auto-approve");

async Task Main()
{
	var client = new HttpClient();
	
	// Get the list of current organizations
	var organizations = await GetOrganizations(client);

	foreach (var org in organizations)
	{
		try
		{
			var organizationLicenseAssignments = await GetLicenseAssignmentsForAnOrganization(client, org.Id);
			
			// should only be one (or none) as an organization can only have one license at a time
			var licenseIdForOrganization = organizationLicenseAssignments.FirstOrDefault();
			if (licenseIdForOrganization != null)
			{
				var licenseApprovalId = licenseIdForOrganization.LicenseId;
							
				// base-radius licenseId is eb858314-fc5f-4f2a-b03c-6b98df631d44
				// and its product id is c59b55b6-25e8-46d4-87b0-ea7374493036
				// according to what shows in table storage
				
				var testLicenseIdApproval = await GetLicenseAutoApproveDetails(client, licenseApprovalId);			
				// need to query to determine what license is assigned to org but no known endpoint...
			}
			
			// reassign the license here
			await ReassignRadiusLicense("pass_in_org_id_for_now");
		}
		catch (Exception ex)
		{
			ex.Dump();
		}
	}
}

public static async Task ReassignRadiusLicense(string organizationId)
{
	// call DeactivateLicenseAssignment to remove existing Radius license

	// call AddOrganizationLicenseAssignment to begin assignment of new Radius license
	
	// call AddOrganizationLicenseAssignment to approve the license
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

	throw new Exception($"{response.StatusCode}: {response.ReasonPhrase}. Route: {route}");
};

/// <summary>
/// Helper method that allows for string parameters
/// </summary>
public static Func<HttpClient, string, Task<TResponse>> GetRequestAsync<TResponse>(string baseUrl, Func<string, string> routeFunc) =>
	(client, routeValue) => GetRequestAsync<TResponse>(baseUrl, routeFunc(routeValue))(client);

/// <summary>
/// Helper method for making HttpPost requests to the Cerberus API
/// </summary>
public static async Task<T> SendRequestAsync<T>(T content, string baseUrl, string route, HttpClient client)
{
	content.Dump();

	if (IsDryRun)
		return content;

	var uri = new Uri(new Uri(baseUrl), route).Dump("Uri");
	var request = new HttpRequestMessage(HttpMethod.Post, uri.Dump());

	request.Content = JsonContent.Create(content);
	request.Headers.Add("x-user-id", "RadiusLicensingLinq"); // does the specific name matter?

	try
	{
		var response = await client.SendAsync(request);

		if (response.IsSuccessStatusCode)
			return content;

		var body = await response.Content.ReadAsStringAsync();

		throw new Exception($"{response.StatusCode}: {response.ReasonPhrase}. Route: {route}. Body: {body}");
	}
	catch (Exception ex)
	{
		ex.Dump();
		throw;
	}
}

/// <summary>
/// Helper method with mapping used for making HttpPost requests to the Cerberus API
/// </summary>
public static async Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest content, string baseUrl, string route, HttpClient client, Func<TRequest, TResponse> dryRunMap = null)
{
	content.Dump();

	if (IsDryRun)
		if (dryRunMap == null)
			return default;
		else
			return dryRunMap(content);

	var uri = new Uri(new Uri(baseUrl), route).Dump("Uri");
	var request = new HttpRequestMessage(HttpMethod.Post, uri);

	request.Content = JsonContent.Create(content);
	request.Headers.Add("x-user-id", "RadiusLicensingLinq"); // does the specific name matter?

	var response = await client.SendAsync(request);

	if (response.IsSuccessStatusCode)
		return await response.Content.ReadFromJsonAsync<TResponse>();

	var errorContent = await response.Content.ReadAsStringAsync();

	errorContent.Dump("Exception Detail");

	throw new Exception($"{response.StatusCode}: {response.ReasonPhrase}");
}

public enum DeactivationReason
{
	Unknown,
	Expired,
	Revoked,
	Mistake,
	DemoComplete
}

// Data models

// GET

public record User(string Id, string FirstName, string LastName, string Email, string MobileNumber);

public record Organization(string Id, string Name, string EmailDomain, string ExternalId, string ImageLinkId);

public record LicenseAssignment(string LicenseAssignmentRequestId, string LicenseId, string OrganizationId, DateTime Expiration);

public record LicenseAssignmentRequest(string Id, string LicenseId, string OrganizationId, int RequestedLength, string RequestedBy, DateTime Requested);

public record License(string Id, string Title, string Description, string ExternalId, string ProductId, int DefaultLicenseLength);

// probably test only; delete later
public record LicenseAutoApproveDetails(string LicenseId, int AutoApproveLimit, DateTime AutoApproveEnabled, string AutoApproveEnabledBy);

// POST

public record AddOrganizationLicenseAssignmentCommand(string organizationId, string licenseId, int requestedLength);

public record AddOrganizationLicenseApprovalCommand(string organizationId, string licenseAssignmentRequestId, DateTime expiration);

public record LicenseAssignmentDeactivation(string LicenseAssignmentRequestId, DateTime Deactivated, string DeactivatedBy, DeactivationReason DeactivationReason, string Reason);

public record DeactivateLicenseRequest(DeactivationReason DeactivationReason, string Reason);