namespace renting_room.Domain.Contracts;

public sealed record NoticeResult(bool ShorterThanNoticePeriod, int NoticeDays, int ActualDays);
