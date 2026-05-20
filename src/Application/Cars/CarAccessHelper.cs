using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars;

internal static class CarAccessHelper
{
    public static async Task<Result<User>> GetCurrentUserAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<User>(Error.Problem("User.NotFound", "Utilizatorul nu a fost găsit."));
        }

        return user;
    }

    public static bool CanManageCar(User user, Car car) =>
        user.Role == UserRole.Admin
        || user.Role == UserRole.CarPoster && car.PostedByUserId == user.Id;

    public static bool CanPostCars(User user) =>
        user.Role is UserRole.Admin or UserRole.CarPoster;

    public static Result ValidateCarManagement(User user, Car? car)
    {
        if (!CanPostCars(user))
        {
            return Result.Failure(Error.Problem("Car.Forbidden", "Nu ai permisiunea de a gestiona mașini."));
        }

        if (car is not null && !CanManageCar(user, car))
        {
            return Result.Failure(Error.Problem("Car.Forbidden", "Nu poți modifica această mașină."));
        }

        return Result.Success();
    }
}
