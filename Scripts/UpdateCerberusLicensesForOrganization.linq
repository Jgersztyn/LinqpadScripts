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
  <Namespace>System.Net</Namespace>
</Query>

// Cerberus API info
public static string _cerberusUrl = "https://localhost:7118"; 
public static bool IsDryRun = false;

// GET API endpoints
public static Func<HttpClient, string, Task<User>> GetUserByEmailAddress = GetRequestAsync<User>(_cerberusUrl, email => $"user/{email}");
public static Func<HttpClient, string, Task<List<User>>> GetOrganizationUserAssignments = GetRequestAsync<List<User>>(_cerberusUrl, organizationId => $"organization/{organizationId}/user");
public static Func<HttpClient, Task<List<Organization>>> GetOrganizations = GetRequestAsync<List<Organization>>(_cerberusUrl, "organization");
public static Func<HttpClient, string, Task<List<LicenseAssignment>>> GetLicenseAssignmentsForAnOrganization = GetRequestAsync<List<LicenseAssignment>>(_cerberusUrl, organizationId => $"organization/{organizationId}/license-assignment");
public static Func<HttpClient, string, Task<List<LicenseAssignmentRequest>>> GetLicenseRequestsForAnOrganization = GetRequestAsync<List<LicenseAssignmentRequest>>(_cerberusUrl, organizationId => $"organization/{organizationId}/license");

// POST API endpoints
public static async Task<AddOrganizationLicenseAssignmentCommand> AddOrganizationLicenseAssignment(string organizationId, string licenseId, int requestedLength, HttpClient client) =>
	await SendRequestAsync(new AddOrganizationLicenseAssignmentCommand(organizationId, licenseId, requestedLength), _cerberusUrl, $"organization/{organizationId}/license/{licenseId}", client);

public static async Task<AddOrganizationLicenseApprovalCommand> AddOrganizationLicenseApproval(string organizationId, string licenseAssignmentRequestId, DateTime expiration, HttpClient client) =>
	await SendRequestAsync(new AddOrganizationLicenseApprovalCommand(organizationId, licenseAssignmentRequestId, expiration), _cerberusUrl, $"organization/{organizationId}/license/{licenseAssignmentRequestId}/approve", client);

public static async Task<LicenseAssignmentDeactivation> DeactivateLicenseAssignment(DeactivateLicenseRequest request, string organizationId, string licenseAssignmentRequestId, HttpClient client) =>
	await SendRequestAsync(new LicenseAssignmentDeactivation(licenseAssignmentRequestId, DateTime.Now, "test-user", request.DeactivationReason, request.Reason), _cerberusUrl, $"organization/{organizationId}/license/{licenseAssignmentRequestId}/deactivate", client);

// test for adding product
//public static async Task<AddProductCommand> AddProduct(string id, string name, string description, HttpClient client) =>
//	await SendRequestAsync(new AddProductCommand(id, id, name, description, ""), _cerberusUrl, "product", client);

async Task Main()
{
	var client = new HttpClient();
	var oldRadiusLicense = "02b8de85-6a50-4cf9-9c87-0b44aba3797b"; // "eb858314-fc5f-4f2a-b03c-6b98df631d44";
	
	// Get the list of current organizations
	var organizations = await GetOrganizations(client);

	foreach (var org in organizations)
	{
		var hasOldRadiusLicense = false;
		var licenseExpiration = DateTime.Now;
		var licenseAssignmentRequestId = "";
		
		try
		{
			var organizationLicenseAssignments = await GetLicenseAssignmentsForAnOrganization(client, org.Id);
			
			if (organizationLicenseAssignments != null)
			{
				foreach (var license in organizationLicenseAssignments)
				{
					// the old base radius licenseId is eb858314-fc5f-4f2a-b03c-6b98df631d44
					// if it exists under this org, we need to make note for it to be reassigned
					if (license.LicenseId == oldRadiusLicense)
					{
						hasOldRadiusLicense = true;
						
						// Keep track of this licenses expiration and assignment id
						licenseExpiration = license.Expiration;
						licenseAssignmentRequestId = license.LicenseAssignmentRequestId;
					}
				}
			}
			
			// reassign the license here
			if (hasOldRadiusLicense)
				await ReassignRadiusLicense(org.Id, licenseAssignmentRequestId, licenseExpiration, client);
		}
		catch (Exception ex)
		{
			ex.Dump();
		}
	}
}

/// <summary>
/// Reassigns one license from an organization to another license
/// </summary>
public static async Task ReassignRadiusLicense(string organizationId, string licenseAssignmentRequestId, DateTime licenseExpiration, HttpClient client)
{
	var newRadiusLicense = "3dc17cbd-0b9a-486b-ae11-83db3b61ff60";
	
	// call DeactivateLicenseAssignment to remove existing Radius license
	var deactivationResult = await DeactivateLicenseAssignment(new DeactivateLicenseRequest(DeactivationReason.Revoked, "Moving to the new Radius license"), organizationId, licenseAssignmentRequestId, client);

	// call AddOrganizationLicenseAssignment to begin assignment of new Radius license
	var organizationAssignmentResult = await AddOrganizationLicenseAssignment(organizationId, newRadiusLicense, 0, client);
	
	// need to get the newly created license assignment request
	var organizationLicenseAssignmentRequests = await GetLicenseRequestsForAnOrganization(client, organizationId);
	
	// filter out license requests for this organization that are not related
	var pendingLicenseAssignment = organizationLicenseAssignmentRequests
		.Where(x => x.State == LicenseAssignmentRequestState.PendingApproval && x.LicenseId == newRadiusLicense);

	// call AddOrganizationLicenseAssignment to approve the license(s)
	foreach (var pendingAdd in pendingLicenseAssignment)
	{
		var organizationApprovalResult = await AddOrganizationLicenseApproval(organizationId, pendingAdd.Id, licenseExpiration, client);
	}
}

/// <summary>
/// Helper method for making HttpGet requests to the Cerberus API
/// </summary>
public static Func<HttpClient, Task<TResponse>> GetRequestAsync<TResponse>(string baseUrl, string route) => async client =>
{
	var uri = new Uri(new Uri(baseUrl), route).Dump("Uri");
	var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{route}");

	request.Headers.Add("x-user-id", "UpdateLicensesLinq");

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
	request.Headers.Add("x-user-id", "RadiusLicensingLinq");

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
	request.Headers.Add("x-user-id", "UpdateLicensesLinq");

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

public enum LicenseAssignmentRequestState
{
    Unknown,
    PendingApproval,
    Approved,
    Denied,
    Deactivated
}

// Data models

// GET

public record User(string Id, string FirstName, string LastName, string Email, string MobileNumber);

public record Organization(string Id, string Name, string EmailDomain, string ExternalId, string ImageLinkId);

public record LicenseAssignment(string LicenseAssignmentRequestId, string LicenseId, string OrganizationId, DateTime Expiration);

public record LicenseAssignmentRequest(string Id, string LicenseId, string OrganizationId, int RequestedLength, string RequestedBy, DateTime Requested, LicenseAssignmentRequestState State);

public record License(string Id, string Title, string Description, string ExternalId, string ProductId, int DefaultLicenseLength);

// POST

public record AddOrganizationLicenseAssignmentCommand(string organizationId, string licenseId, int requestedLength);

public record AddOrganizationLicenseApprovalCommand(string organizationId, string licenseAssignmentRequestId, DateTime expiration);

public record LicenseAssignmentDeactivation(string LicenseAssignmentRequestId, DateTime Deactivated, string DeactivatedBy, DeactivationReason DeactivationReason, string Reason);

public record DeactivateLicenseRequest(DeactivationReason DeactivationReason, string Reason);