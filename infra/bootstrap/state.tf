locals {
  state_containers = toset([
    "tfstate-bootstrap",
    "tfstate-shared",
    "tfstate-staging",
    "tfstate-production",
    "tfstate-email",
  ])
}

resource "azurerm_resource_group" "tfstate" {
  name     = "rg-climbon-tfstate"
  location = var.location
}

resource "azurerm_storage_account" "tfstate" {
  name                            = "stclimbontfstate"
  resource_group_name             = azurerm_resource_group.tfstate.name
  location                        = azurerm_resource_group.tfstate.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  https_traffic_only_enabled      = true
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  allow_nested_items_to_be_public = false

  depends_on = [azurerm_resource_provider_registration.this["Microsoft.Storage"]]
}

resource "azurerm_storage_container" "tfstate" {
  for_each = local.state_containers

  name                  = each.key
  storage_account_id    = azurerm_storage_account.tfstate.id
  container_access_type = "private"
}

resource "azurerm_role_assignment" "owner_state" {
  scope                = azurerm_storage_account.tfstate.id
  role_definition_name = "Storage Blob Data Owner"
  principal_id         = var.owner_object_id
  principal_type       = "User"
}
