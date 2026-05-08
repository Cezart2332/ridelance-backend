using Application.Abstractions;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.InviteContabil;

public sealed record InviteContabilCommand(string FullName, string Email) : ICommand<Guid>;

internal sealed class InviteContabilCommandHandler(
    IApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer) : ICommandHandler<InviteContabilCommand, Guid>
{
    public async Task<Result<Guid>> Handle(InviteContabilCommand request, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AnyAsync(u => u.Email == request.Email, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict("Users.EmailAlreadyInUse", "Acest email este deja folosit."));
        }

        // Generate a random 8-character password
        string password = Guid.NewGuid().ToString("N")[..8];
        string passwordHash = passwordHasher.Hash(password);

        // Split FullName into First and Last name (rough estimation)
        string[] nameParts = request.FullName.Split(' ', 2);
        string firstName = nameParts.Length > 1 ? nameParts[0] : request.FullName;
        string lastName = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            FirstName = firstName,
            LastName = lastName,
            PasswordHash = passwordHash,
            Role = UserRole.Contabil,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Send Email using MJML
        string subject = "Cont RIDElance - Invitație Contabil";
        string mjml = $@"
<mjml>
  <mj-head>
    <mj-title>{subject}</mj-title>
    <mj-font name=""Inter"" href=""https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap"" />
    <mj-attributes>
      <mj-all font-family=""Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif"" />
      <mj-text font-size=""16px"" color=""#374151"" line-height=""26px"" />
      <mj-section padding=""0"" />
    </mj-attributes>
  </mj-head>
  <mj-body background-color=""#F9FAFB"">
    <mj-spacer height=""40px"" />
    
    <mj-section padding=""0 20px"">
      <mj-column>
        <mj-text font-size=""24px"" font-weight=""800"" color=""#111827"" align=""center"">
          RIDE<span style=""color: #5CCBF5;"">lance</span>
        </mj-text>
      </mj-column>
    </mj-section>
    
    <mj-spacer height=""30px"" />
    
    <mj-section padding=""0 20px"">
      <mj-column background-color=""#ffffff"" padding=""30px"" border-radius=""12px"">
        <mj-text font-size=""28px"" font-weight=""700"" color=""#111827"" align=""center"" padding-bottom=""30px"">
          Bun venit pe platformă!
        </mj-text>
        
        <mj-text>
          Salutare, <strong>{request.FullName}</strong>,
        </mj-text>
        
        <mj-text>
          Ai fost invitat să te alături platformei <strong>RIDElance</strong> în calitate de <strong>Contabil</strong>. Suntem entuziasmați să te avem alături!
        </mj-text>
        
        <mj-spacer height=""20px"" />
        
        <mj-text font-weight=""600"" color=""#374151"">
          Datele tale de autentificare:
        </mj-text>
        
        <mj-table background-color=""#F9FAFB"" padding=""20px"">
          <tr>
            <td style=""padding-bottom: 5px; color: #6B7280; font-size: 14px;"">Email</td>
          </tr>
          <tr>
            <td style=""padding-bottom: 15px; color: #111827; font-weight: 600;"">{request.Email}</td>
          </tr>
          <tr>
            <td style=""padding-bottom: 5px; color: #6B7280; font-size: 14px;"">Parolă temporară</td>
          </tr>
          <tr>
            <td style=""color: #111827; font-weight: 600;"">
              <span style=""font-family: monospace; background-color: #F3F4F6; padding: 4px 8px; border-radius: 4px;"">{password}</span>
            </td>
          </tr>
        </mj-table>
        
        <mj-spacer height=""30px"" />
        
        <mj-button background-color=""#5CCBF5"" color=""#ffffff"" font-size=""16px"" font-weight=""700"" href=""https://ridelance.ro/auth"" border-radius=""50px"" inner-padding=""16px 40px"" width=""100%"">
          Accesează Contul
        </mj-button>
        
        <mj-spacer height=""30px"" />
        
        <mj-divider border-width=""1px"" border-color=""#F3F4F6"" />
        
        <mj-spacer height=""20px"" />
        
        <mj-text font-size=""14px"" color=""#6B7280"" line-height=""22px"">
          <strong>Sfat:</strong> Pentru securitatea datelor tale, îți recomandăm să schimbi această parolă temporară imediat după prima autentificare din secțiunea <strong>Profil</strong>.
        </mj-text>
      </mj-column>
    </mj-section>
    
    <mj-section padding=""20px"">
      <mj-column>
        <mj-text align=""center"" font-size=""13px"" color=""#9CA3AF"">
          &copy; {DateTime.UtcNow.Year} RIDElance Digital Solutions. Toate drepturile rezervate.
        </mj-text>
      </mj-column>
    </mj-section>
    
    <mj-spacer height=""40px"" />
  </mj-body>
</mjml>";

        string htmlBody = mjmlRenderer.Render(mjml);

        await emailService.SendEmailAsync(request.Email, subject, htmlBody, cancellationToken);

        return user.Id;
    }
}
