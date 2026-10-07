terraform {
  required_version = "~> 1.16.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "5.8.0"
    }
  }

  # Applied first with local state (README); this backend is enabled by the state migration.
  # A backend block cannot read variables, so the account name is repeated from state.tf.
  backend "azurerm" {
    storage_account_name = "stclimbontfstate"
    container_name       = "tfstate-bootstrap"
    key                  = "bootstrap.tfstate"
    use_azuread_auth     = true
  }
}

provider "azurerm" {
  features {}

  resource_provider_registrations = "none"
  storage_use_azuread             = true
}

data "azurerm_subscription" "current" {}
