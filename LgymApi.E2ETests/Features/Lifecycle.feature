Feature: Lifecycle

@serial @Lifecycle @web-lifecycle
Scenario: lifecycle-probe-a
    Then the lifecycle stack is ready
    And the lifecycle browser storage is empty

@serial @Lifecycle @web-lifecycle
Scenario: lifecycle-probe-b
    Then the lifecycle stack is ready
    And the lifecycle browser storage is empty
