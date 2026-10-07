# NOK, the subscription's billing currency (C15).
resource "azurerm_consumption_budget_subscription" "monthly" {
  name            = "climbon-monthly"
  subscription_id = data.azurerm_subscription.current.id
  amount          = 55
  time_grain      = "Monthly"

  time_period {
    start_date = "2026-10-01T00:00:00Z"
  }

  notification {
    enabled        = true
    operator       = "GreaterThan"
    threshold      = 100
    threshold_type = "Actual"
    contact_emails = concat([var.admin_email], var.budget_extra_emails)
  }

  depends_on = [azurerm_resource_provider_registration.this["Microsoft.Consumption"]]
}
