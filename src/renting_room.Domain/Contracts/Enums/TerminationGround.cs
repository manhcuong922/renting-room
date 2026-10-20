namespace renting_room.Domain.Contracts;

public enum TerminationGround
{
    RentArrears3Months,
    WrongPurpose,
    UnauthorizedRenovation,
    Other,

    IndefiniteTermNotice
}
