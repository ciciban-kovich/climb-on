locals {
  resource_providers = toset([
    "Microsoft.App",
    "Microsoft.DBforPostgreSQL",
    "Microsoft.KeyVault",
    "Microsoft.Storage",
    "Microsoft.OperationalInsights",
    "Microsoft.Insights",
    "Microsoft.ManagedIdentity",
    "Microsoft.Consumption",
  ])

  # Registered on the subscription before the first apply (checked 2026-10-06).
  resource_providers_preregistered = toset([
    "Microsoft.Consumption",
  ])
}

resource "azurerm_resource_provider_registration" "this" {
  for_each = local.resource_providers

  name = each.key
}

import {
  for_each = local.resource_providers_preregistered

  to = azurerm_resource_provider_registration.this[each.key]
  id = "${data.azurerm_subscription.current.id}/providers/${each.key}"
}
