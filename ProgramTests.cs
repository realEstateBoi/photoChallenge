using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;

namespace DownloadHomePhotos;

[TestFixture]
public class ProgramTests
{
    private readonly HttpClient _httpClient;

    public ProgramTests()
    {
        _httpClient = new HttpClient();
    }

    [Test]
    [NonParallelizable]
    public async Task GetHousesAsync_ReturnsListOfHouses()
    {
        // Arrange
        const int page = 1;

        // Act
        var houses = await Program.GetHousesAsync(_httpClient, page);

        // Assert
        Assert.That(houses, Is.Not.Null);
        Assert.That(houses, Is.InstanceOf(typeof(List<House>)));
        Assert.That(houses.Count, Is.GreaterThan(0));
    }

    [Test]
    [NonParallelizable]
    public async Task DownloadPhotosInBatchesAsync_DownloadsPhotos()
    {
        // Arrange
        var houses = new List<House>
        {
            new House { Id = 1, Address = "Test Address 1", PhotoUrl = "https://example.com/photo1.jpg" },
            new House { Id = 2, Address = "Test Address 2", PhotoUrl = "https://example.com/photo2.jpg" }
        };
        var photoUrlMap = new Dictionary<string, string>();

        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[0]) })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[0]) });
        
        var httpClient = new HttpClient(mockHttpMessageHandler.Object);

        // Act
        await Program.DownloadPhotosInBatchesAsync(httpClient, houses, photoUrlMap);
        
        // Assert
        foreach (var house in houses)
        {
            var photoExtension = Path.GetExtension(house.PhotoUrl);
            var photoName = $"{house.Id}-{house.Address}{photoExtension}";
            Assert.That(File.Exists(Program.BuildPhotoFilePath(photoName)));
            File.Delete(Program.BuildPhotoFilePath(photoName)); // Clean up
        }
    }
    
    [Test]
    [NonParallelizable]
    public async Task GetHousesAsync_RetriesOnFailedRequest()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"houses\": [{ \"Id\": 1, \"Address\": \"Test Address\", \"PhotoUrl\": \"https://example.com/photo.jpg\" }], \"ok\": true}") });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var page = 1;

        // Act
        var houses = await Program.GetHousesAsync(httpClient, page);

        // Assert
        Assert.That(houses, Is.Not.Null);
        Assert.That(houses, Is.InstanceOf(typeof(List<House>)));
        Assert.That(houses.Count, Is.EqualTo(1));
    }
    
    [Test]
    [NonParallelizable]
    public void WriteMissingPhotosToFile_WritesToCorrectFile()
    {
        // Arrange
        var missingPhotosDictionary = new Dictionary<string, string>
        {
            {"1-Test Address 1.jpg", "https://example.com/photo1.jpg"},
            {"2-Test Address 2.jpg", "https://example.com/photo2.jpg"},
        };

        var expectedLines = new List<string>
        {
            "https://example.com/photo1.jpg",
            "https://example.com/photo2.jpg",
        };

        var mockStreamWriter = new Mock<StreamWriter>("missing_photos.txt");
        mockStreamWriter.Setup(sw => sw.WriteLine(It.IsAny<string>()))
            .Callback<string>(line =>
            {
                // Assert that the written line matches one of the expected lines
                Assert.That(line, Does.Contain(expectedLines));
            });

        // Act
        Program.WriteMissingPhotosToFile(missingPhotosDictionary);
    }
    
    [Test]
    [NonParallelizable]
    public async Task DownloadMissingPhotosAsync_SuccessfullyDownloadsPhotos()
    {
        // Arrange
        var missingPhotosDictionary = new Dictionary<string, string>
        {
            {"1-Test Address 1.jpg", "https://example.com/photo1.jpg"},
            {"2-Test Address 2.jpg", "https://example.com/photo2.jpg"},
        };

        // Create the 'missing_photos.txt' file
        await File.AppendAllTextAsync(Program.BuildPhotoFilePath("missing_photos.txt"), $"{JsonSerializer.Serialize(missingPhotosDictionary)}{Environment.NewLine}");

        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[0]) })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[0]) });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);

        // Act
        await Program.DownloadMissingPhotosAsync(httpClient);
        
        // Assert
        Assert.That(File.Exists(Program.BuildPhotoFilePath("1-Test Address 1.jpg")));
        Assert.That(File.Exists(Program.BuildPhotoFilePath("2-Test Address 2.jpg")));
    }

    [TearDown]
    [SetUp]
    public void SetupAndTearDown()
    {
        string workingDirectory = Environment.CurrentDirectory;
        string? projectDirectory = Directory.GetParent(workingDirectory)?.Parent?.Parent?.FullName;
        if (projectDirectory != null)
        {
            string photoFilePath = Path.Combine(projectDirectory, "photos");
            Directory.CreateDirectory(photoFilePath);
        }

        if (File.Exists(Program.BuildPhotoFilePath("missing_photos.txt")))
        {
            File.Delete(Program.BuildPhotoFilePath("missing_photos.txt"));
        }
    }

}