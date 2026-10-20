namespace renting_room.Domain.Billing;

public sealed record StandardPeriod(DateOnly Start, DateOnly End, DateOnly Month, bool IsTransition, int BaseDays, int AdjustDays)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;

    public decimal RentMonths => 1m + (decimal)AdjustDays / BaseDays;

    public int DeviationDays => Days - BaseDays;
}
