namespace renting_room.Requests;

public sealed record UpdateDataRetentionRequest(int RetentionMonths, bool AutoAnonymize);
