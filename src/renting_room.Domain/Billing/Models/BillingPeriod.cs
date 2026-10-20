namespace renting_room.Domain.Billing;

public sealed record BillingPeriod(DateOnly Start, DateOnly End, DateOnly Month)
{
    public string BillingMonth => $"{Month.Year:D4}-{Month.Month:D2}";

    public int Days => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}
