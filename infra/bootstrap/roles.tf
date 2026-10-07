# ci-plan's only subscription-scope role. Actions beyond Reader's are added only by the
# design §7 plan check (task 30); no data action may grant blob reads.
resource "azurerm_role_definition" "climbon_plan" {
  name              = "climbon-plan"
  scope             = data.azurerm_subscription.current.id
  description       = "Reader plus the actions a refresh of the climb-on roots needs."
  assignable_scopes = [data.azurerm_subscription.current.id]

  permissions {
    actions      = ["*/read"]
    data_actions = []
  }
}

locals {
  rg = {
    shared     = azurerm_resource_group.shared.id
    staging    = azurerm_resource_group.staging.id
    production = azurerm_resource_group.production.id
  }
  container = { for name, c in azurerm_storage_container.tfstate : name => c.id }

  # Every role assignment of the CI identities and the admin's account; verify.sh checks the
  # live assignments against the same set.
  ci_role_assignments = merge(
    {
      for name in ["tfstate-bootstrap", "tfstate-shared", "tfstate-staging", "tfstate-production"] :
      "ci-plan/reader/${name}" => { principal = "ci-plan", role = "Storage Blob Data Reader", scope = local.container[name] }
    },
    {
      for role in ["Contributor", "Role Based Access Control Administrator", "Key Vault Crypto Officer", "Storage Blob Data Contributor"] :
      "ci-staging/${role}/rg-staging" => { principal = "ci-staging", role = role, scope = local.rg.staging }
    },
    {
      "ci-staging/state/tfstate-staging" = { principal = "ci-staging", role = "Storage Blob Data Contributor", scope = local.container["tfstate-staging"] }
    },
    merge([
      for env in ["production", "shared", "staging"] : {
        for role in ["Contributor", "Role Based Access Control Administrator"] :
        "ci-production/${role}/rg-${env}" => { principal = "ci-production", role = role, scope = local.rg[env] }
      }
    ]...),
    {
      for role in ["Key Vault Crypto Officer", "Storage Blob Data Contributor"] :
      "ci-production/${role}/rg-production-data" => { principal = "ci-production", role = role, scope = local.rg.production }
    },
    {
      for name in ["tfstate-shared", "tfstate-production", "tfstate-staging"] :
      "ci-production/state/${name}" => { principal = "ci-production", role = "Storage Blob Data Contributor", scope = local.container[name] }
    },
  )

  admin_role_assignments = {
    for env in ["staging", "production"] :
    "admin/secrets/rg-${env}" => { role = "Key Vault Secrets Officer", scope = local.rg[env] }
  }
}

resource "azurerm_role_assignment" "ci_plan" {
  scope              = data.azurerm_subscription.current.id
  role_definition_id = azurerm_role_definition.climbon_plan.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.ci["ci-plan"].principal_id
  principal_type     = "ServicePrincipal"
}

resource "azurerm_role_assignment" "ci" {
  for_each = local.ci_role_assignments

  scope                = each.value.scope
  role_definition_name = each.value.role
  principal_id         = azurerm_user_assigned_identity.ci[each.value.principal].principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "admin" {
  for_each = local.admin_role_assignments

  scope                = each.value.scope
  role_definition_name = each.value.role
  principal_id         = var.admin_object_id
  principal_type       = "User"
}
