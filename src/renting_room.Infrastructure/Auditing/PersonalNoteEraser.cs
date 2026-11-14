using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Renters;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Auditing;

/// <summary>
/// RT-BR-06 (05/11/2026, L-17): xóa trắng / che dữ liệu cá nhân trong ô ghi chú tự do của HĐ, phiếu, phiếu thu… và bỏ các trường đó khỏi
/// <c>audit_logs</c>. SQL thẳng (không qua ChangeTracker) ⇒ không sinh dòng nhật ký mới chứa lại giá trị cũ; chạy trong transaction của lệnh
/// ẩn danh. Tên bảng / cột là hằng trong code, giá trị truyền bằng tham số.
/// </summary>
internal sealed class PersonalNoteEraser(AppDbContext db, ICurrentUser currentUser) : IPersonalNoteEraser
{
    public const string Erased = "(đã xóa khi ẩn danh)";

    private const string InContracts = "contract_id = ANY(@contracts)";

    /// <summary>Ô ghi chú tự do theo HĐ: (bảng, điều kiện thuộc các HĐ @contracts, cột).</summary>
    private static readonly (string Table, string Scope, string[] Columns)[] ContractNotes =
    [
        ("contracts", "id = ANY(@contracts)", ["note", "deposit_note", "signed_document_note", "holdover_note", "termination_note", "cancel_reason"]),
        ("contract_occupants", InContracts, ["note"]),
        ("contract_assets", InContracts, ["note"]),
        ("contract_rent_terms", InContracts, ["note"]),
        ("contract_vehicles", InContracts, ["note"]),
        ("invoices", InContracts, ["note", "void_reason", "refund_note"]),
        ("invoice_lines", "invoice_id IN (SELECT id FROM invoices WHERE contract_id = ANY(@contracts))", ["note"]),
        ("invoice_adjustments", InContracts, ["reason"]),
        ("room_charges", InContracts, ["reason", "cancel_reason"]),
        ("contract_billing_gaps", InContracts, ["note"]),
        ("payments", InContracts, ["note", "reverse_reason"]),
        ("collection_reductions", InContracts, ["reason"]), // M08 PM-BR-38 — lý do giảm số đã thu
    ];

    /// <summary>Tên trường JSON (camelCase) của các cột trên trong <c>audit_logs.changes</c>.</summary>
    private static readonly string[] AuditKeys =
        ["note", "depositNote", "signedDocumentNote", "holdoverNote", "terminationNote", "cancelReason", "voidReason", "refundNote", "reason", "reverseReason"];

    public async Task EraseAsync(PersonalNoteErasure erasure, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new InvalidOperationException("Erasing personal notes requires an organization context.");
        var clear = erasure.ClearContractIds.ToArray();
        var mask = erasure.MaskContractIds.Except(clear).ToArray();
        var pattern = PersonalDataMask.Pattern(erasure.Identifiers);

        if (clear.Length > 0)
            foreach (var (table, scope, columns) in ContractNotes)
                await RunAsync(Update(table, scope, columns.Select(c => $"{c} = CASE WHEN {c} IS NULL THEN NULL ELSE @erased END")),
                    organizationId, erasure.RenterId, clear, pattern, cancellationToken);
        if (mask.Length > 0 && pattern is not null)
            foreach (var (table, scope, columns) in ContractNotes)
                await RunAsync(Update(table, scope, columns.Select(c => $"{c} = regexp_replace({c}, @pattern, @mask, 'gi')")),
                    organizationId, erasure.RenterId, mask, pattern, cancellationToken);

        // Ghi chú về chính người đó (dòng người ở, đồng ý xử lý dữ liệu): luôn xóa trắng.
        await RunAsync(Update("contract_occupants", "renter_id = @renter", ["note = CASE WHEN note IS NULL THEN NULL ELSE @erased END"]),
            organizationId, erasure.RenterId, [], pattern, cancellationToken);
        await RunAsync(Update("renter_consents", "renter_id = @renter", ["note = CASE WHEN note IS NULL THEN NULL ELSE @erased END"]),
            organizationId, erasure.RenterId, [], pattern, cancellationToken);

        var all = clear.Concat(mask).ToArray();
        var rows = string.Join(" UNION ALL ", ContractNotes.Select(n => $"SELECT id FROM {n.Table} WHERE {n.Scope}")
            .Append("SELECT id FROM contract_occupants WHERE renter_id = @renter")
            .Append("SELECT id FROM renter_consents WHERE renter_id = @renter"));
        var audit = "UPDATE audit_logs SET changes = changes - @keys WHERE organization_id = @organization AND changes IS NOT NULL AND entity_id IN (" + rows + ")";
        await RunAsync(audit, organizationId, erasure.RenterId, all, pattern, cancellationToken);
    }

    private static string Update(string table, string scope, IEnumerable<string> assignments) =>
        "UPDATE " + table + " SET " + string.Join(", ", assignments) + " WHERE organization_id = @organization AND " + scope;

    private Task RunAsync(string sql, Guid organizationId, Guid renterId, Guid[] contracts, string? pattern, CancellationToken cancellationToken)
    {
        NpgsqlParameter[] parameters =
        [
            new("organization", organizationId),
            new("renter", renterId),
            new("contracts", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = contracts },
            new("erased", Erased),
            new("mask", PersonalDataMask.Mask),
            new("pattern", NpgsqlDbType.Text) { Value = (object?)pattern ?? DBNull.Value },
            new("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = AuditKeys },
        ];
        return db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }
}
