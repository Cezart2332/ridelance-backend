using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.DeleteCarImage;

public sealed record DeleteCarImageCommand(Guid CarId, Guid ImageId) : ICommand;

internal sealed class DeleteCarImageCommandHandler(IApplicationDbContext context)
    : ICommandHandler<DeleteCarImageCommand>
{
    public async Task<Result> Handle(DeleteCarImageCommand command, CancellationToken cancellationToken)
    {
        CarImage? image = await context.CarImages
            .FirstOrDefaultAsync(i => i.Id == command.ImageId && i.CarId == command.CarId, cancellationToken);

        if (image is null)
        {
            return Result.Failure(Error.NotFound("CarImage.NotFound", "Imaginea nu a fost găsită."));
        }

        // Delete physical file
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
