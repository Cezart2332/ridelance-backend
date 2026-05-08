using Application.Users.Login;
using Application.Abstractions.Messaging;
using SharedKernel;

namespace Application.Users.Impersonate;

public sealed record ImpersonateUserCommand(Guid TargetUserId) : ICommand<LoginResponse>;
