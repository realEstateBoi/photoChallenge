using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DownloadHomePhotos;

internal class Program
{
    private const string ApiUrl = "http://app-homevision-staging.herokuapp.com/api_project/houses";
    private const int DefaultPerPage = 10;
    private const int NumPages = 10;

    private static async Task Main(string[] args)
    {
        // Record the start time
        DateTime startTime = DateTime.Now;
        
        using var client = new HttpClient();
        
        // if there is a missing_photos.txt it means we're in missing photos mode since the last run had missing photos
        // and we just want to download the missing photos so we don't have to use up more API calls
        if (File.Exists(BuildPhotoFilePath("missing_photos.txt")))
        {
            Console.WriteLine($"Running in missing photos mode");
            await DownloadMissingPhotosAsync(client);
            return;
        }

        
        var allHouses = new List<House>();
        var photoUrlMap = new Dictionary<string, string>();

        for (int page = 1; page <= NumPages; page++)
        {
            Console.WriteLine($"Getting houses on page {page}");
            var houses = await GetHousesAsync(client, page);
            
            Console.WriteLine($"Number of houses on page {page}: {houses.Count}");
            allHouses.AddRange(houses);

            await DownloadPhotosInBatchesAsync(client, houses, photoUrlMap);
        }

        var countOfHousesMissingPhotoUrl = allHouses.Count(house => string.IsNullOrEmpty(house.PhotoUrl));
            
        Console.WriteLine($"Total number of houses seen in process: {allHouses.Count}"); ;
        Console.WriteLine($"Total number of houses missing photoURL: {countOfHousesMissingPhotoUrl}");
        Console.WriteLine($"Total number of photo download issue resulting in writing of missing_photos.txt: {photoUrlMap.Count}");
        WriteMissingPhotosToFile(photoUrlMap);
        
        // Record the end time
        DateTime endTime = DateTime.Now;

        // Calculate the duration
        TimeSpan duration = endTime - startTime;

        // Display the duration
        Console.WriteLine($"Main method took {duration.TotalSeconds} seconds to run.");
    }
    
    public static async Task<List<House>> GetHousesAsync(HttpClient client, int page)
    {
        string url = $"{ApiUrl}?page={page}&per_page={DefaultPerPage}";
        int retryCount = 0;
        while (retryCount < 5)
        {
            HttpResponseMessage response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<HousesResponse>(jsonContent, options)?.Houses ?? [];
            }

            Console.WriteLine($"Failed to fetch houses data from page {page}. Status code: {response.StatusCode}");
            retryCount++;
            if (retryCount == 5)
            {
                Console.WriteLine($"Failed to fetch houses data from page {page} after 5 attempts. Terminating the application since something is super duper wrong and we should be asking the vendor for a refund at this point. Someone call Jerry.");
                Environment.Exit(1);
            }
            Console.WriteLine($"Retrying... Attempt {retryCount} of 5.");
            await Task.Delay(1000); // Wait for 1 second before retrying
        }
        return null; // Return null if failed to fetch data after 5 attempts
    }

    public static async Task DownloadPhotosInBatchesAsync(HttpClient client, List<House> houses, Dictionary<string, string> photoUrlMap)
    {
        var downloadTasks = new List<Task>();

        foreach (var house in houses)
        {
            var photoUrl = house.PhotoUrl;
            if (string.IsNullOrEmpty(photoUrl))
            {
                Console.WriteLine($"PhotoUrl is missing or empty for: {house.Id}-{house.Address}");
                continue;
            }

            var photoExtension = Path.GetExtension(photoUrl);
            var photoName = $"{house.Id}-{house.Address}{photoExtension}";

            downloadTasks.Add(DownloadPhotoAsync(client, photoUrl, photoName, photoUrlMap));
        }

        await Task.WhenAll(downloadTasks);
    }

    private static async Task DownloadPhotoAsync(HttpClient client, string photoUrl, string photoName, IDictionary<string, string> photoUrlMap)
    {
        try
        {
            byte[] photoBytes = await client.GetByteArrayAsync(photoUrl);
            
            var photoFilePath = BuildPhotoFilePath(photoName);
            await File.WriteAllBytesAsync(photoFilePath, photoBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to download photo for: {photoName}. Error: {ex.Message}");
            photoUrlMap[photoName] = photoUrl; // Mark the photo download as failed so it can be retried on next run
        }
    }

    public static string BuildPhotoFilePath(string photoName)
    {
        // build a file path to the project directory /photos so we don't have to dig through bin/debug etc.
        string? workingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location));
        string? projectDirectory = Directory.GetParent(workingDirectory)?.Parent?.FullName;
        string photoFilePath = photoName;
        if (projectDirectory != null)
        {
            photoFilePath = Path.Combine(projectDirectory, "photos");
            Console.WriteLine($"Saving photos to directory {photoFilePath}");
            Directory.CreateDirectory(photoFilePath);
            photoFilePath = Path.Combine(photoFilePath, photoName);
        }

        return photoFilePath;
    }

    public static async void WriteMissingPhotosToFile(Dictionary<string, string> failedPhotoDownloads)
    {
        if (failedPhotoDownloads.Count > 0)
            await File.AppendAllTextAsync(BuildPhotoFilePath("missing_photos.txt"), $"{JsonSerializer.Serialize(failedPhotoDownloads)}{Environment.NewLine}");
    }

    internal static async Task DownloadMissingPhotosAsync(HttpClient client)
    {
        // Read the JSON string from the file
        string json = await File.ReadAllTextAsync(BuildPhotoFilePath("missing_photos.txt"));

        // Deserialize the JSON string to a dictionary
       var missingPhotos = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

       if (missingPhotos != null)
       {
           foreach (var missingPhoto in missingPhotos)
           {
               try
               {
                   var photoBytes = await client.GetByteArrayAsync(missingPhoto.Value);
                   await File.WriteAllBytesAsync(BuildPhotoFilePath(missingPhoto.Key), photoBytes);
               }
               catch (Exception ex)
               {
                   Console.WriteLine($"Failed to download photo for: {missingPhoto.Value}. Error: {ex.Message}");
               }
           }
       }
    }
}

class HousesResponse
{
    public List<House>? Houses { get; set; }
    public bool Ok { get; set; }
}

class House
{
    public int Id { get; set; }
    public string Address { get; set; }
    public string Homeowner { get; set; }
    public int Price { get; set; }
    public string PhotoUrl { get; set; }
}