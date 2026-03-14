content = r"""@model RestaurantManagementSystem.Models.GSTBreakupReportViewModel
@using RestaurantManagementSystem.Models.Authorization
@{
    ViewData["Title"] = "GST Breakup Report";
    var reportPermissions = ViewBag.ReportPermissions as PermissionSet ?? new PermissionSet();
}

<style>
    .report-header-gradient {
        background: linear-gradient(135deg, #1e3c72 0%, #2a5298 50%, #3b6fd4 100%);
        padding: 1.5rem;
        border-radius: 0.5rem 0.5rem 0 0;
        color: white;
        box-shadow: 0 4px 6px -1px rgba(0,0,0,0.15);
    }
    .report-header-gradient h4 { font-weight: 700; margin: 0; }
    .report-header-gradient .subtitle { opacity: .9; font-size: .92rem; margin-top: .2rem; }
    .summary-tile { padding: 1rem; border-radius: .5rem; box-shadow: 0 2px 6px rgba(0,0,0,0.08); height: 110px; display: flex; flex-direction: column; justify-content: center; }
    .summary-tile .label { font-size: .82rem; font-weight: 600; opacity: .92; margin-bottom: .2rem; }
    .summary-tile .value { font-weight: 800; font-size: 1.5rem; line-height: 1.1; }
    .tile-indigo  { background: linear-gradient(135deg,#5b6bf5,#7bb1ff); color:#fff; }
    .tile-cyan    { background: linear-gradient(135deg,#14b8a6,#2dd4c4); color:#fff; }
    .tile-amber   { background: linear-gradient(135deg,#f59e0b,#fbbf24); color:#fff; }
    .tile-green   { background: linear-gradient(135deg,#16a34a,#22c55e); color:#fff; }
    .tile-slate   { background: linear-gradient(135deg,#475569,#64748b); color:#fff; }
    .tile-dark    { background: linear-gradient(135deg,#1e293b,#334155); color:#fff; }
    @@media (max-width:768px) {
        .summary-tile { height:90px; }
        .summary-tile .value { font-size:1.25rem; }
    }
    @@media print { .no-print { display:none !important; } }
</style>

<div class="card shadow-sm mb-4">
    <div class="report-header-gradient">
        <div class="d-flex align-items-center justify-content-between">
            <div>
                <h4><i class="fas fa-file-invoice-dollar me-2"></i>GST Breakup Report</h4>
                <div class="subtitle">Taxable value, CGST / SGST and invoice totals by order</div>
            </div>
            <div><i class="fas fa-percent fa-3x opacity-50"></i></div>
        </div>
    </div>

    <div class="card-body">
        <div class="alert alert-info mb-4 py-2">
            <i class="fas fa-info-circle me-2"></i>
            <strong>Formula:</strong> Taxable Value = Subtotal &minus; Discount &nbsp;|&nbsp;
            <strong>GST</strong> = Taxable Value &times; GST% &nbsp;|&nbsp;
            <strong>Invoice Total</strong> = Taxable Value + Total GST
        </div>

        <form asp-action="GSTBreakup" method="post" id="gstFilterForm">
            <div class="row g-3 align-items-end mb-3">
                <div class="col-lg-2 col-md-3 col-sm-6">
                    <label asp-for="Filter.StartDate" class="form-label fw-semibold">From Date</label>
                    <input asp-for="Filter.StartDate" class="form-control" type="date" />
                </div>
                <div class="col-lg-2 col-md-3 col-sm-6">
                    <label asp-for="Filter.EndDate" class="form-label fw-semibold">To Date</label>
                    <input asp-for="Filter.EndDate" class="form-control" type="date" />
                </div>
                @if (Model.IsMainBranchAdmin)
                {
                    <div class="col-lg-3 col-md-6">
                        <label class="form-label fw-semibold">Branches</label>
                        <div class="dropdown" id="branchSelectorDropdown">
                            <button class="btn btn-outline-info dropdown-toggle w-100 text-start" type="button"
                                    data-bs-auto-close="outside" data-bs-toggle="dropdown" aria-expanded="false">
                                <i class="fas fa-code-branch me-1"></i>
                                <span id="branchDropdownLabel">All Branches</span>
                            </button>
                            <div class="dropdown-menu p-2" style="min-width:220px">
                                <div class="d-flex gap-2 mb-1">
                                    <button type="button" class="btn btn-sm btn-link p-0 small" onclick="selectAllBranches(true)">All</button>
                                    <span>&middot;</span>
                                    <button type="button" class="btn btn-sm btn-link p-0 small" onclick="selectAllBranches(false)">None</button>
                                </div>
                                <hr class="my-1">
                                @foreach (var b in Model.Branches)
                                {
                                    <div class="form-check">
                                        <input class="form-check-input branch-chk" type="checkbox" name="Filter.SelectedBranchIds"
                                               value="@b.Value" id="bchk_@b.Value"
                                               @(Model.Filter.SelectedBranchIds.Contains(int.Parse(b.Value)) ? "checked" : "")>
                                        <label class="form-check-label" for="bchk_@b.Value">@b.Text</label>
                                    </div>
                                }
                            </div>
                        </div>
                    </div>
                }
            </div>

            <div class="d-flex flex-wrap gap-2 align-items-center border-top pt-3 no-print">
                <button type="submit" class="btn btn-success">
                    <i class="fas fa-sync-alt me-1"></i>Refresh Report
                </button>
                <div class="vr mx-1 d-none d-sm-block"></div>
                @if (reportPermissions.CanExport)
                {
                    <button type="button" id="exportCsvBtn" class="btn btn-outline-secondary">
                        <i class="fas fa-file-csv me-1"></i>CSV
                    </button>
                    <button type="button" id="exportExcelBtn" class="btn btn-outline-success">
                        <i class="fas fa-file-excel me-1"></i>Excel
                    </button>
                    <button type="button" id="exportPdfBtn" class="btn btn-outline-danger">
                        <i class="fas fa-file-pdf me-1"></i>PDF
                    </button>
                    <div class="vr mx-1 d-none d-sm-block"></div>
                }
                <button type="button" id="printBtn" class="btn btn-outline-primary">
                    <i class="fas fa-print me-1"></i>Print
                </button>
            </div>
        </form>
    </div>

    <div class="card-body border-top">
        <div class="row g-3 mb-4">
            <div class="col-lg-2 col-md-4 col-sm-6">
                <div class="summary-tile tile-indigo">
                    <div class="label"><i class="fas fa-receipt me-1"></i>Invoices</div>
                    <div class="value">@Model.Summary.InvoiceCount</div>
                </div>
            </div>
            <div class="col-lg-2 col-md-4 col-sm-6">
                <div class="summary-tile tile-cyan">
                    <div class="label"><i class="fas fa-rupee-sign me-1"></i>Taxable Value</div>
                    <div class="value">&#8377;@Model.Summary.TotalTaxableValue.ToString("N2")</div>
                </div>
            </div>
            <div class="col-lg-2 col-md-4 col-sm-6">
                <div class="summary-tile tile-amber">
                    <div class="label"><i class="fas fa-tags me-1"></i>Discount</div>
                    <div class="value">&#8377;@Model.Summary.TotalDiscount.ToString("N2")</div>
                </div>
            </div>
            <div class="col-lg-2 col-md-4 col-sm-6">
                <div class="summary-tile tile-green">
                    <div class="label"><i class="fas fa-percentage me-1"></i>CGST</div>
                    <div class="value">&#8377;@Model.Summary.TotalCGST.ToString("N2")</div>
                </div>
            </div>
            <div class="col-lg-2 col-md-4 col-sm-6">
                <div class="summary-tile tile-slate">
                    <div class="label"><i class="fas fa-percentage me-1"></i>SGST</div>
                    <div class="value">&#8377;@Model.Summary.TotalSGST.ToString("N2")</div>
                </div>
            </div>
            <div class="col-lg-2 col-md-4 col-sm-6">
                <div class="summary-tile tile-dark">
                    <div class="label"><i class="fas fa-file-invoice me-1"></i>Invoice Total</div>
                    <div class="value">&#8377;@Model.Summary.NetAmount.ToString("N2")</div>
                </div>
            </div>
        </div>

        @if (Model.Rows.Any())
        {
            <div class="alert alert-success mb-3 py-2">
                <i class="fas fa-filter me-2"></i>
                <strong>Period:</strong> @Model.Filter.StartDate?.ToString("dd-MMM-yyyy") to @Model.Filter.EndDate?.ToString("dd-MMM-yyyy")
                &nbsp;|&nbsp;<strong>Records:</strong> @Model.Rows.Count
            </div>
        }

        <div class="table-responsive">
            <table class="table table-striped table-hover table-sm align-middle" id="gstBreakupTable">
                <thead class="table-dark">
                    <tr>
                        <th class="text-nowrap">Date/Time</th>
                        <th>Invoice #</th>
                        <th>Bill No</th>
                        @if (Model.IsMainBranchAdmin) { <th>Branch</th> }
                        <th>Type</th>
                        <th>Table</th>
                        <th class="text-end">Subtotal</th>
                        <th class="text-end">Discount</th>
                        <th class="text-end" style="background:rgba(255,193,7,.18)">Taxable Value</th>
                        <th class="text-end">GST %</th>
                        <th class="text-end">CGST %</th>
                        <th class="text-end">CGST &#8377;</th>
                        <th class="text-end">SGST %</th>
                        <th class="text-end">SGST &#8377;</th>
                        <th class="text-end" style="background:rgba(25,135,84,.18)">Total GST</th>
                        <th class="text-end" style="background:rgba(13,202,240,.18)">Invoice Total</th>
                    </tr>
                </thead>
                <tbody>
                @if (!Model.Rows.Any())
                {
                    <tr>
                        <td colspan="@(Model.IsMainBranchAdmin ? 16 : 15)" class="text-center text-muted py-5">
                            <i class="fas fa-search fa-2x mb-2 d-block"></i>
                            No records found for the selected date range.
                        </td>
                    </tr>
                }
                else
                {
                    foreach (var row in Model.Rows)
                    {
                        decimal subtotal = row.TaxableValue + row.DiscountAmount;
                        <tr>
                            <td class="small text-nowrap">@row.PaymentDateFormatted</td>
                            <td class="fw-semibold">@row.OrderNumber</td>
                            <td class="small text-nowrap">@row.BillNo</td>
                            @if (Model.IsMainBranchAdmin) { <td class="small text-nowrap">@row.BranchName</td> }
                            <td>
                                @if (row.OrderType == "Bar")
                                { <span class="badge bg-primary">BAR</span> }
                                else
                                { <span class="badge bg-secondary">Foods</span> }
                            </td>
                            <td class="small">@row.TableNumber</td>
                            <td class="text-end small text-secondary">&#8377;@subtotal.ToString("N2")</td>
                            <td class="text-end small text-danger">@(row.DiscountAmount > 0 ? $"-&#8377;{row.DiscountAmount:N2}" : "-")</td>
                            <td class="text-end fw-bold">&#8377;@row.TaxableValue.ToString("N2")</td>
                            <td class="text-end">
                                <span class="badge @(row.GSTPercentage >= 20 ? "bg-danger" : "bg-info text-dark")">
                                    @row.GSTPercentage.ToString("N1")%
                                </span>
                            </td>
                            <td class="text-end small">@row.CGSTPercentage.ToString("N2")%</td>
                            <td class="text-end">&#8377;@row.CGSTAmount.ToString("N2")</td>
                            <td class="text-end small">@row.SGSTPercentage.ToString("N2")%</td>
                            <td class="text-end">&#8377;@row.SGSTAmount.ToString("N2")</td>
                            <td class="text-end fw-bold">&#8377;@row.TotalGST.ToString("N2")</td>
                            <td class="text-end fw-bold">&#8377;@row.InvoiceTotal.ToString("N2")</td>
                        </tr>
                    }
                }
                </tbody>
                @if (Model.Rows.Any())
                {
                    <tfoot class="table-secondary fw-bold">
                        <tr>
                            <td colspan="@(Model.IsMainBranchAdmin ? 8 : 7)" class="text-end">TOTALS:</td>
                            <td class="text-end">&#8377;@Model.Summary.TotalTaxableValue.ToString("N2")</td>
                            <td colspan="2"></td>
                            <td class="text-end">&#8377;@Model.Summary.TotalCGST.ToString("N2")</td>
                            <td></td>
                            <td class="text-end">&#8377;@Model.Summary.TotalSGST.ToString("N2")</td>
                            <td class="text-end">&#8377;@Model.Summary.TotalGST.ToString("N2")</td>
                            <td class="text-end">&#8377;@Model.Summary.NetAmount.ToString("N2")</td>
                        </tr>
                    </tfoot>
                }
            </table>
        </div>
    </div>
</div>

@section Scripts {
    <script>
        function buildExportParams() {
            const p = new URLSearchParams();
            const from = document.querySelector('[name="Filter.StartDate"]')?.value || '';
            const to   = document.querySelector('[name="Filter.EndDate"]')?.value   || '';
            if (from) p.set('startDate', from);
            if (to)   p.set('endDate', to);
            const checked = Array.from(document.querySelectorAll('.branch-chk:checked')).map(cb => cb.value);
            if (checked.length) p.set('branchIds', checked.join(','));
            return p.toString();
        }

        document.getElementById('exportCsvBtn')?.addEventListener('click', function () {
            const rows = Array.from(document.querySelectorAll('#gstBreakupTable tr'));
            const csv = rows.map(r =>
                Array.from(r.children).map(c => '"' + c.innerText.trim().replace(/"/g, '""') + '"').join(',')
            ).join('\n');
            const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            const from = document.querySelector('[name="Filter.StartDate"]')?.value || 'start';
            const to   = document.querySelector('[name="Filter.EndDate"]')?.value   || 'end';
            a.download = 'GSTBreakup_' + from + '_to_' + to + '.csv';
            a.click();
        });

        document.getElementById('exportExcelBtn')?.addEventListener('click', function () {
            const qs = buildExportParams();
            window.open('@Url.Action("GSTBreakupExcel", "Reports")' + (qs ? '?' + qs : ''), '_blank');
        });

        document.getElementById('exportPdfBtn')?.addEventListener('click', function () {
            const qs = buildExportParams();
            window.open('@Url.Action("GSTBreakupPdf", "Reports")' + (qs ? '?' + qs : ''), '_blank');
        });

        document.getElementById('printBtn')?.addEventListener('click', function () {
            window.print();
        });

        function selectAllBranches(checked) {
            document.querySelectorAll('.branch-chk').forEach(cb => cb.checked = checked);
            updateBranchLabel();
        }

        function updateBranchLabel() {
            const total   = document.querySelectorAll('.branch-chk').length;
            const checked = document.querySelectorAll('.branch-chk:checked').length;
            const label   = document.getElementById('branchDropdownLabel');
            if (!label) return;
            label.textContent = (checked === 0 || checked === total) ? 'All Branches' : checked + ' Branch(es) Selected';
        }

        document.querySelectorAll('.branch-chk').forEach(cb => cb.addEventListener('change', updateBranchLabel));
        updateBranchLabel();
    </script>
}
"""

path = '/Users/abhikporel/dev/Restaurantapp/RestaurantManagementSystem/RestaurantManagementSystem/Views/Reports/GSTBreakup.cshtml'
with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
print(f'OK: wrote {len(content)} chars, {content.count(chr(10))} lines')
