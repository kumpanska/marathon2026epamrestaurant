Feature: Group pre-order participants

  The reservation owner can review the guests who joined, their dishes and quantities,
  each guest's subtotal, and the total for the whole group.

  Background:
    Given a customer owns a group reservation

  Rule: The owner can review the group's participants

    @api @smoke @group-preorder-smoke
    Scenario: The owner sees guest baskets, confirmation state, and totals
      Given guest "AQA-Sample-Alice" has joined the group
      And guest "AQA-Sample-Alice" adds 1 portions of the dish
      And guest "AQA-Sample-Bob" has joined the group
      When the owner views the group participants
      Then the owner sees 2 guest participants
      And guest "AQA-Sample-Alice" is listed with 1 portions of the dish
      And guest "AQA-Sample-Alice" has the correct subtotal for 1 portions
      And guest "AQA-Sample-Alice" is not marked as submitted
      And guest "AQA-Sample-Bob" is listed with an unconfirmed empty basket
      And the group total equals the sum of participant totals
      When guest "AQA-Sample-Alice" adds 2 portions of the dish
      And guest "AQA-Sample-Alice" submits their basket
      And the owner views the group participants
      Then guest "AQA-Sample-Alice" is listed with 3 portions of the dish
      And guest "AQA-Sample-Alice" has the correct subtotal for 3 portions
      And guest "AQA-Sample-Alice" is marked as submitted
      And guest "AQA-Sample-Bob" is listed with an unconfirmed empty basket
      And the group total equals the sum of participant totals

    @api @known-defect
    Scenario: An empty group has no participants and a zero total
      When the owner views the group participants
      Then the group has no participants and a total of zero

  Rule: Only the reservation owner may review participants

    @api @negative
    Scenario: A waiter cannot view another customer's group participants
      When a waiter who does not own the reservation views the group participants
      Then the waiter is forbidden from viewing the group participants

    @api @negative
    Scenario: Another customer cannot view the group participants
      When another customer views the group participants
      Then the non-owner is forbidden from viewing the group participants

    @api @negative
    Scenario: A guest cannot view the group participants
      Given guest "AQA-Sample-Guest" has joined the group
      When guest "AQA-Sample-Guest" views the group participants
      Then the guest is refused from viewing the group participants

    @api @negative
    Scenario: An unauthenticated visitor cannot view group participants
      When an unauthenticated visitor views the group participants
      Then the unauthenticated visitor is refused

    @api @negative
    Scenario: A group that does not exist cannot be viewed
      When the owner views a group that does not exist
      Then the missing group is not found
