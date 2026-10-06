locals {
  # Federated subjects per CI identity (C52), without the "repo:<owner>/<name>:" prefix.
  ci_subjects = {
    "ci-plan"       = ["pull_request", "environment:drift"]
    "ci-staging"    = ["environment:staging"]
    "ci-production" = ["environment:production"]
  }

  ci_credentials = merge([
    for identity, subjects in local.ci_subjects : {
      for subject in subjects : "${identity}/${subject}" => {
        identity = identity
        subject  = subject
      }
    }
  ]...)
}

resource "azurerm_user_assigned_identity" "ci" {
  for_each = local.ci_subjects

  name                = each.key
  resource_group_name = azurerm_resource_group.tfstate.name
  location            = azurerm_resource_group.tfstate.location

  depends_on = [azurerm_resource_provider_registration.this["Microsoft.ManagedIdentity"]]
}

resource "azurerm_federated_identity_credential" "ci" {
  for_each = local.ci_credentials

  name                      = "github-${replace(each.value.subject, ":", "-")}"
  user_assigned_identity_id = azurerm_user_assigned_identity.ci[each.value.identity].id
  issuer                    = "https://token.actions.githubusercontent.com"
  audience                  = ["api://AzureADTokenExchange"]
  subject                   = "repo:${var.github_repo}:${each.value.subject}"
}
