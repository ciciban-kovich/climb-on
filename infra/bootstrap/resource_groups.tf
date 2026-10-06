resource "azurerm_resource_group" "shared" {
  name     = "rg-climbon-shared"
  location = var.location
}

resource "azurerm_resource_group" "staging" {
  name     = "rg-climbon-staging"
  location = var.location
}

resource "azurerm_resource_group" "production" {
  name     = "rg-climbon-production"
  location = var.location
}
