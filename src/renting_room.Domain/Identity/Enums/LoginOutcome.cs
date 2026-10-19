using System.Text;
using renting_room.Domain.Common;

namespace renting_room.Domain.Identity;

public enum LoginOutcome
{
    Success,
    UnknownUser,
    WrongPassword,
    LockedOut,
    TemporaryPasswordExpired,
    OrganizationSuspended
}
