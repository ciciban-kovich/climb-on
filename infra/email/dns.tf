# TEM exports record names as absolute names ("<x>.mail-staging.<domain>."); a record in the
# registered domain's zone takes the part before the zone.
locals {
  zone_suffix = ".${var.domain}"

  record_names = {
    for env, sub in var.environments : env => {
      host  = sub
      dkim  = trimsuffix(trimsuffix(scaleway_tem_domain.env[env].dkim_name, "."), local.zone_suffix)
      dmarc = trimsuffix(trimsuffix(scaleway_tem_domain.env[env].dmarc_name, "."), local.zone_suffix)
    }
  }
}

resource "scaleway_domain_record" "spf" {
  for_each = var.environments

  dns_zone = var.domain
  name     = local.record_names[each.key].host
  type     = "TXT"
  data     = scaleway_tem_domain.env[each.key].spf_value
}

resource "scaleway_domain_record" "dkim" {
  for_each = var.environments

  dns_zone = var.domain
  name     = local.record_names[each.key].dkim
  type     = "TXT"
  data     = scaleway_tem_domain.env[each.key].dkim_config
}

resource "scaleway_domain_record" "mx" {
  for_each = var.environments

  dns_zone = var.domain
  name     = local.record_names[each.key].host
  type     = "MX"
  data     = scaleway_tem_domain.env[each.key].mx_config
  priority = scaleway_tem_domain.env[each.key].mx_priority
}

resource "scaleway_domain_record" "dmarc" {
  for_each = var.environments

  dns_zone = var.domain
  name     = local.record_names[each.key].dmarc
  type     = "TXT"
  data     = scaleway_tem_domain.env[each.key].dmarc_config
}
