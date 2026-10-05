# Uploaded expenses and monthly review

Client uploads and accountant reviews use the same expense form. AI reads the document and proposes a category. Deductibility is calculated by `DeductibilityService`, using the PFA's vehicle setting at the payment date; the client-supplied percentage is not used to calculate the ledger.

The document is initially a draft. Confirmation records either a cash payment, an existing booked bank payment, or an unpaid invoice. An unpaid invoice produces no RJIP payment and no REF deduction. A bank payment must match the exact amount and date from the selected OpenBanking transaction. No second bank payment is created.

Paid uploads have an `ExpenseDocument` linking the original uploaded file to a `LedgerEntry`. RJIP reads the full paid amount; REF reads the deductible business portion. Personal articles are excluded. A different beneficiary CUI keeps the payment under review and outside REF. Fixed asset review and closed-period guards continue to apply.

An accountant can correct an existing expense and payment together, with a reason. Bank amount/date cannot be overwritten from a receipt. Approval is saved with the expense/payment in the same database save. Rejection marks the linked payment as requiring review, excluding it from REF. Corrections in a closed period require the existing accounting correction workflow.

The accountant month selector includes the current Romanian calendar month. Document and expense review are available during that month. Monthly declaration processing, generation and transitions are blocked by the backend until the month has ended; the current-month page does not offer declaration or closing actions.

Received e-Factura invoices are synchronized from ANAF, not uploaded to ANAF by the buyer. A matching supplier CUI, invoice number, issue date and total allows the ANAF original and an uploaded copy to share one payment. This works whether ANAF synchronization happens before or after upload. Ambiguous matches and partial payments remain for manual review. A repeated upload with the same confirmed document identity cannot create another cash payment.

The synchronization button uses the existing connected ANAF service and bank ledger import, with owner/assigned-accountant scope checks. It does not establish a new OAuth authorization or submit a supplier invoice. Missing ANAF configuration/authorization is shown as a note.

Previously uploaded expenses are preserved. They can be opened through **Verifică / modifică** to classify and associate their actual payment. There is no automatic backfill that assumes an invoice was paid or invents a cash payment.

Deployment requires migration `20261006120000_AddUploadedExpenseCategories` for the additional categories. Unknown expenses use `OTHER_BUSINESS` with a special-rule review, rather than an automatic 100% deduction.
