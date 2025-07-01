# Azure Custom Vision Infrastructure as Code

This directory contains Terraform code to provision Azure Custom Vision resources for both training and prediction, with secure storage of credentials in Azure Key Vault.

## Resources Provisioned

- **Azure Cognitive Services (Custom Vision)**
  - Training resource (`ai-rkw-vision-training`)
  - Prediction resource (`ai-rkw-vision-prediction`)
- **Azure Key Vault Secrets**
  - Stores API keys and endpoints for both resources

## Resource Overview

| Resource Type                | Name/Key                         | Purpose                                 |
|------------------------------|----------------------------------|-----------------------------------------|
| azurerm_cognitive_account    | rkw-training                     | Custom Vision Training instance         |
| azurerm_cognitive_account    | rkw-prediction                   | Custom Vision Prediction instance       |
| azurerm_key_vault_secret     | api-key-1-training               | Training primary API key                |
| azurerm_key_vault_secret     | api-key-2-training               | Training secondary API key              |
| azurerm_key_vault_secret     | api-endpoint-training            | Training endpoint URL                   |
| azurerm_key_vault_secret     | api-key-1-prediction             | Prediction primary API key              |
| azurerm_key_vault_secret     | api-key-2-prediction             | Prediction secondary API key            |
| azurerm_key_vault_secret     | api-endpoint-prediction          | Prediction endpoint URL                 |

## Usage

1. **Prerequisites**
   - Azure subscription
   - Terraform installed
   - Existing Azure Resource Group and Key Vault

2. **Initialize Terraform**
   ```sh
   terraform init
   ```

3. **Plan the Deployment**
   ```sh
   terraform plan
   ```

4. **Apply the Configuration**
   ```sh
   terraform apply
   ```

## Variables

- The configuration expects the following resources to exist:
  - `azurerm_resource_group.rkw`
  - `azurerm_key_vault.rkw`
  - `local.tags` for resource tagging

## Outputs

- API keys and endpoints are stored as secrets in the specified Azure Key Vault.

## Notes

- Both Cognitive Services accounts use the `S0` SKU.
- Custom subdomain names are set for both training and prediction endpoints.
- Follow [Azure Terraform best practices](https://learn.microsoft.com/en-us/azure/developer/terraform/best-practices) for