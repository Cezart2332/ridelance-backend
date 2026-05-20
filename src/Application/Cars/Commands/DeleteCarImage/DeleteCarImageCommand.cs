using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.DeleteCarImage;

public sealed record DeleteCarImageCommand(Guid CarId, Guid ImageId) : ICommand;

internal sealed class DeleteCarImageCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<DeleteCarImageCommand>
{
    public async Task<Result> Handle(DeleteCarImageCommand command, CancellationToken cancellationToken)
    {
        CarImage? image = await context.CarImages
            .Include(i => i.Car)
            .FirstOrDefaultAsync(i => i.Id == command.ImageId && i.CarId == command.CarId, cancellationToken);

        if (image is null)
        {
            return Result.Failure(Error.NotFound("CarImage.NotFound", "Imaginea nu a fost găsită."));
        }

        Result<User> userResult = await CarAccessHelper.GetCurrentUserAsync(context, userContext, cancellationToken);
        if (userResult.IsFailure)
        {
            return Result.Failure(userResult.Error);
        }

        Result access = CarAccessHelper.ValidateCarManagement(userResult.Value, image.Car);
        if (access.IsFailure)
        {
            return access;
        }

        string filePath = Path.Combine("uploads", "cars", image.FileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        context.CarImages.Remove(image);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
