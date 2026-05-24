using Application.Abstractions.Messaging;
using Domain.Cars;

namespace Application.Cars.Commands.RecordCarAnalytics;

public sealed record RecordCarAnalyticsCommand(Guid CarId, CarAnalyticsEventType EventType) : ICommand;
