namespace renting_room.Domain.Contracts;

public enum TerminationReason
{
    Expired,
    MutualAgreement,
    LesseeUnilateral,
    LessorUnilateral,
    RoomTransfer,

    Abandoned
}
