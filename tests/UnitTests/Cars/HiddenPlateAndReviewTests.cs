using Application.Abstractions.Services;
using Application.Cars;
using Application.Cars.Commands.PaidExtras;
using Domain.Cars;
using Domain.Notifications;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>Numărul ascuns e o opțiune plătită, iar anunțurile firmelor ajung la admin cu notificare.</summary>
public sealed class HiddenPlateAndReviewTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly byte[] BlurredJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 9, 9];

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly string _directory = Path.Combine("uploads", "cars");
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (CarImage image in _db.CarImages.ToList())
        {
            File.Delete(Path.Combine(_directory, image.FileName));
        }

        foreach (string file in _files)
        {
            File.Delete(file);
        }

        _db.Dispose();
    }

    private CarImage Photo(bool plateHidden, DateTime? blurredAt = null)
    {
        Directory.CreateDirectory(_directory);
        var car = new Car { Id = Guid.NewGuid(), Brand = "Tesla", Model = "Model 3", PlateHidden = plateHidden };
        var image = new CarImage { Id = Guid.NewGuid(), CarId = car.Id, Car = car, FileName = $"{Guid.NewGuid()}.jpg", PlateBlurredAtUtc = blurredAt };
        image.Url = $"/uploads/cars/{image.FileName}";
        string path = Path.Combine(_directory, image.FileName);
        File.WriteAllBytes(path, Jpeg);
        _files.Add(path);
        _db.CarImages.Add(image);
        _db.SaveChanges();
        return image;
    }

    [Fact]
    public async Task OnlyPaidCars_GetTheirPhotosBlurred_AndOnlyOnce()
    {
        CarImage unpaid = Photo(plateHidden: false);
        CarImage paid = Photo(plateHidden: true);
        CarImage alreadyBlurred = Photo(plateHidden: true, blurredAt: DateTime.UtcNow.AddDays(-1));
        string paidOldName = paid.FileName;
        var plates = new RecordingPlates();
        var handler = new BlurHiddenPlatePhotosCommandHandler(_db, plates);

        (await handler.Handle(new BlurHiddenPlatePhotosCommand(), default)).Value.ShouldBe(1);

        // Doar poza mașinii plătite a trecut prin blurare, cu blurarea cerută explicit.
        plates.Calls.ShouldBe([true]);
        paid.PlateBlurredAtUtc.ShouldNotBeNull();
        // Fișier nou (cel vechi poate fi în cache cu numărul la vedere), iar cel vechi dispare.
        paid.FileName.ShouldNotBe(paidOldName);
        paid.Url.ShouldBe($"/uploads/cars/{paid.FileName}");
        File.Exists(Path.Combine(_directory, paidOldName)).ShouldBeFalse();
        (await File.ReadAllBytesAsync(Path.Combine(_directory, paid.FileName))).ShouldBe(BlurredJpeg);

        // Neplătită: poza rămâne exact cum a fost încărcată.
        unpaid.PlateBlurredAtUtc.ShouldBeNull();
        (await File.ReadAllBytesAsync(Path.Combine(_directory, unpaid.FileName))).ShouldBe(Jpeg);
        (await File.ReadAllBytesAsync(Path.Combine(_directory, alreadyBlurred.FileName))).ShouldBe(Jpeg);

        // A doua trecere nu mai are ce face.
        (await handler.Handle(new BlurHiddenPlatePhotosCommand(), default)).Value.ShouldBe(0);
        plates.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ACarSentForReview_NotifiesEveryAdmin_Once()
    {
        var admin = new User { Id = Guid.NewGuid(), Email = "admin@ridelance.ro", Role = UserRole.Admin };
        var secondAdmin = new User { Id = Guid.NewGuid(), Email = "admin2@ridelance.ro", Role = UserRole.Admin };
        var owner = new User { Id = Guid.NewGuid(), Email = "flota@example.ro", Role = UserRole.CarPoster };
        _db.Users.AddRange(admin, secondAdmin, owner);
        var car = new Car { Id = Guid.NewGuid(), Brand = "Tesla", Model = "Model 3", PostedByUserId = owner.Id };
        _db.Cars.Add(car);
        await _db.SaveChangesAsync();

        await CarReviewNotifications.AddAsync(_db, car, edited: false, default);
        await _db.SaveChangesAsync();

        List<Notification> sent = await _db.Notifications.ToListAsync();
        sent.Select(n => n.UserId).ShouldBe([admin.Id, secondAdmin.Id], ignoreOrder: true);
        sent.ShouldAllBe(n => n.Type == NotificationTypes.CarListingReview && n.RelatedUserId == owner.Id && n.Text.Contains("Tesla Model 3"));

        // Firma mai editează de două ori înainte ca adminul să se uite: tot o notificare rămâne.
        await CarReviewNotifications.AddAsync(_db, car, edited: true, default);
        await _db.SaveChangesAsync();
        (await _db.Notifications.CountAsync()).ShouldBe(2);

        // După ce un admin a citit-o, următoarea modificare îl anunță din nou — doar pe el.
        sent.Single(n => n.UserId == admin.Id).IsRead = true;
        await _db.SaveChangesAsync();
        await CarReviewNotifications.AddAsync(_db, car, edited: true, default);
        await _db.SaveChangesAsync();
        (await _db.Notifications.CountAsync(n => n.UserId == admin.Id)).ShouldBe(2);
        (await _db.Notifications.CountAsync(n => n.UserId == secondAdmin.Id)).ShouldBe(1);
    }

    private sealed class RecordingPlates : ILicensePlateDetectionService
    {
        public List<bool> Calls { get; } = [];

        public Task<byte[]> ProcessImageAsync(Stream imageStream, bool blurPlates, CancellationToken cancellationToken = default)
        {
            Calls.Add(blurPlates);
            return Task.FromResult(BlurredJpeg);
        }
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
