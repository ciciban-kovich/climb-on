terraform {
  required_version = "~> 1.16.0"

  required_providers {
    scaleway = {
      source  = "scaleway/scaleway"
      version = "2.84.0"
    }
  }

  # The owner's Azure CLI login reaches this container through Storage Blob Data Owner on the
  # state account (infra/bootstrap); no CI identity can read it.
  backend "azurerm" {
    storage_account_name = "stclimbontfstate"
    container_name       = "tfstate-email"
    key                  = "email.tfstate"
    use_azuread_auth     = true
  }
}

# Credentials come only from SCW_ACCESS_KEY and SCW_SECRET_KEY in the owner's environment.
provider "scaleway" {
  organization_id = var.organization_id
  region          = "fr-par"
}
