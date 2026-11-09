using System.Text.Json;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>La emisión como se responde, y los mensajes de ARCA como se guardan (un arreglo de código y texto).</summary>
internal static class ArcaVoucherRowPolicy
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ArcaVoucherResponse Response(ArcaVoucher voucher) => new(voucher.Id, voucher.RequestKey, voucher.State, voucher.ReviewReason, voucher.Class,
        voucher.Kind, voucher.VoucherType, voucher.PointOfSale, voucher.Number, voucher.Cae, voucher.CaeDueOn, voucher.IssuedOn, voucher.RequestedOn,
        voucher.Total, voucher.FailureCode, voucher.WaitedForArca, voucher.NextAttemptAtUtc, voucher.Revision, voucher.PdfStorageKey, voucher.PdfSha256,
        voucher.PdfSize);

    public static ArcaVoucherResponse Response(ArcaVoucherRow row) => new(row.Id, row.RequestKey, row.State, row.ReviewReason, row.Class, row.Kind,
        row.VoucherType, row.PointOfSale, row.Number, row.Cae, row.CaeDueOn, row.IssuedOn, row.RequestedOn, row.Total, row.FailureCode, row.WaitedForArca,
        row.NextAttemptAtUtc, row.Revision, row.PdfStorageKey, row.PdfSha256, row.PdfSize);

    public static string? Messages(ArcaAuthorization authorization) =>
        authorization.Errors.Count + authorization.Observations.Count == 0
            ? null
            : JsonSerializer.Serialize(authorization.Errors.Concat(authorization.Observations), Json);

    /// <summary>El motivo estable de un rechazo o de una duda: el primer código de ARCA, con el prefijo del servicio.</summary>
    public static string FailureCode(ArcaAuthorization authorization, string fallback) =>
        authorization.Errors.Concat(authorization.Observations).FirstOrDefault() is { } first ? $"wsfe.{first.Code}" : fallback;
}
